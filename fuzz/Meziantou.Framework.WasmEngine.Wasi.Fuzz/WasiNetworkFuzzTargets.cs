using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.Components;
using Meziantou.Framework.WasmEngine.Wasi.Components;

namespace Meziantou.Framework.WasmEngine.Wasi.Fuzz;

/// <summary>
/// The targets of <c>wasi:sockets</c> and <c>wasi:http</c> of WASI 0.3:
/// <list type="bullet">
/// <item>The syntax of the HTTP fields, paths, authorities and schemes never throws, and agrees with its definitions.</item>
/// <item>The addresses of the sockets (<c>ip-socket-address</c>) round-trip through <see cref="IPEndPoint"/>.</item>
/// <item>The sockets of a <see cref="WasiInMemoryNetwork"/> only fail with a <see cref="SocketException"/>, never bind two
/// sockets to the same port of the same address, and deliver the datagrams and the bytes they send.</item>
/// </list>
/// </summary>
internal static class WasiNetworkFuzzTargets
{
    private const int MaxOperations = 64;

    private static readonly string[] HttpTokens = ["\n", "/", "?", "#", "%20", ":", "@", "[", "]", "::1", "127.0.0.1", "example.com", ":8080", ":99999", "*", "http", "https", "coap+tcp", "content-type", "transfer-encoding", "\r\n", "\t", " ", "\u0080", "é", "<", "{", "\""];

    private static readonly IPAddress[] Addresses = [IPAddress.Loopback, IPAddress.Any, IPAddress.IPv6Loopback, IPAddress.IPv6Any, IPAddress.Parse("127.0.0.2"), IPAddress.Parse("192.0.2.1")];

    private static readonly int[] Ports = [0, 1000, 1001, 49152];

    public static IEnumerable<FuzzTarget> All =>
    [
        new("wasi-http-syntax", HttpSyntaxTarget) { Seeds = GetHttpSeeds, Dictionary = HttpTokens },
        new("wasi-socket-address", SocketAddressTarget) { Seeds = GetAddressSeeds },
        new("wasi-memory-network", MemoryNetworkTarget) { Seeds = GetNetworkSeeds },
    ];

    private static IEnumerable<FuzzSeed> GetHttpSeeds()
    {
        // The inputs of the unit tests of the syntax
        yield return FuzzSeed.FromText("names", "content-type\nX-Custom_1!\n\nbad name\nbad:name");
        yield return FuzzSeed.FromText("paths", "/a/b?c=d&e\n?foo\n/café\na/b\n/a b\n/<\n/a#b");
        yield return FuzzSeed.FromText("authorities", "example.com:8080\nuser@[::1]:443\nexample.com/path\nexample.com:99999\n[not-ipv6]");
        yield return FuzzSeed.FromText("schemes", "coap+tcp\n1http\nHTTPS");
    }

    private static void HttpSyntaxTarget(ReadOnlySpan<byte> data)
    {
        foreach (var text in FuzzInput.ReadText(data).Split('\n'))
        {
            var isToken = text.Length > 0 && text.All(WasiHttpSyntax.IsTokenCharacter);
            FuzzAssert.Equal(isToken, WasiHttpSyntax.IsValidFieldName(text), "IsValidFieldName(" + FuzzAssert.Format(text) + ")");

            if (WasiHttpSyntax.IsValidPathWithQuery(text))
            {
                var path = WasiHttpSyntax.RemoveFragment(text);
                FuzzAssert.True(WasiHttpSyntax.IsValidPathWithQuery(path), "The path without fragment of a valid path is valid: " + FuzzAssert.Format(text));
                FuzzAssert.False(path.Any(c => c is <= ' ' or '\x7F' or '#'), "A valid path has no spaces, control characters or fragment: " + FuzzAssert.Format(text));
            }

            if (WasiHttpSyntax.IsValidAuthority(text))
            {
                FuzzAssert.False(text.Any(c => c is '/' or '?' or '#' or <= ' '), "A valid authority has no path, query, fragment or space: " + FuzzAssert.Format(text));
            }

            FuzzAssert.Equal(text.Length > 0 && Uri.CheckSchemeName(text), WasiHttpSyntax.IsValidScheme(text), "IsValidScheme(" + FuzzAssert.Format(text) + ")");
            _ = WasiHttpSyntax.IsValidFieldValue(System.Text.Encoding.Latin1.GetBytes(text));
        }
    }

    private static IEnumerable<FuzzSeed> GetAddressSeeds()
    {
        yield return new FuzzSeed("ipv4", (byte[])[0, 0x1F, 0x90, 127, 0, 0, 1]);
        yield return new FuzzSeed("ipv6", (byte[])[1, 0x01, 0xBB, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 3]);
    }

    /// <summary>The input is the family (even: IPv4), the port (2 bytes), the address (4 or 16 bytes), and the scope of an IPv6 address (4 bytes).</summary>
    private static void SocketAddressTarget(ReadOnlySpan<byte> data)
    {
        var isV6 = (FuzzInput.ReadByte(ref data) & 1) == 1;
        var port = (ushort)((FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data));
        var parts = new ComponentValue[isV6 ? 8 : 4];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = isV6 ? ComponentValue.FromU16((ushort)((FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data))) : ComponentValue.FromU8(FuzzInput.ReadByte(ref data));
        }

        var scope = (uint)(FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8) | (FuzzInput.ReadByte(ref data) << 16) | (FuzzInput.ReadByte(ref data) << 24));
        var value = isV6
            ? ComponentValue.FromCase("ipv6", ComponentValue.FromRecord(ComponentValue.FromU16(port), ComponentValue.FromU32(0), ComponentValue.FromTuple(parts), ComponentValue.FromU32(scope)))
            : ComponentValue.FromCase("ipv4", ComponentValue.FromRecord(ComponentValue.FromU16(port), ComponentValue.FromTuple(parts)));

        var endPoint = WasiSockets.ToEndPoint(value);
        FuzzAssert.Equal(port, endPoint.Port, "The port");
        FuzzAssert.Equal(isV6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, endPoint.AddressFamily, "The family");
        var roundTrip = WasiSockets.ToEndPoint(WasiSockets.FromEndPoint(endPoint));
        FuzzAssert.True(roundTrip.Equals(endPoint) && (!isV6 || roundTrip.Address.ScopeId == endPoint.Address.ScopeId), "The address round-trips: " + endPoint + " gave " + roundTrip);
    }

    private enum NetworkOperation
    {
        CreateTcp,
        CreateUdp,
        Bind,
        Listen,
        Connect,
        SendDatagram,
        Dispose,
        Count,
    }

    private static IEnumerable<FuzzSeed> GetNetworkSeeds()
    {
        // A listener and a connection; two UDP sockets that exchange a datagram; two sockets on the same port
        yield return new FuzzSeed("tcp", (byte[])[0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 4, 1, 0, 0]);
        yield return new FuzzSeed("udp", (byte[])[1, 0, 0, 1, 0, 0, 2, 0, 0, 0, 2, 1, 0, 0, 5, 1, 0, 0]);
        yield return new FuzzSeed("conflict", (byte[])[0, 0, 0, 0, 0, 0, 2, 0, 0, 1, 2, 1, 1, 1]);
    }

    /// <summary>The input is a sequence of operations: a byte of the operation, a byte of the socket, and two bytes of the address and the port.</summary>
    private static void MemoryNetworkTarget(ReadOnlySpan<byte> data)
    {
        var network = new WasiInMemoryNetwork();
        var tcp = new List<WasiTcpSocket>();
        var udp = new List<WasiUdpSocket>();
        var listeners = new HashSet<WasiTcpSocket>();
        var accepted = new HashSet<WasiTcpSocket>();
        try
        {
            for (var count = 0; count < MaxOperations && !data.IsEmpty; count++)
            {
                var operation = (NetworkOperation)(FuzzInput.ReadByte(ref data) % (int)NetworkOperation.Count);
                var index = FuzzInput.ReadByte(ref data);
                var address = FuzzInput.Pick(Addresses, FuzzInput.ReadByte(ref data));
                var endPoint = new IPEndPoint(address, FuzzInput.Pick(Ports, FuzzInput.ReadByte(ref data)));
                try
                {
                    Run(operation, index, endPoint);
                }
                catch (SocketException)
                {
                    // An error of the operation (an address in use, a connection refused, ...)
                }

                // The accepted sockets share the port of their listener
                CheckBindings(tcp.Where(socket => !accepted.Contains(socket)).Select(socket => socket.LocalEndPoint));
                CheckBindings(udp.Select(socket => socket.LocalEndPoint));
            }
        }
        finally
        {
            tcp.ForEach(socket => socket.Dispose());
            udp.ForEach(socket => socket.Dispose());
        }

        void Run(NetworkOperation operation, byte index, IPEndPoint endPoint)
        {
            switch (operation)
            {
                case NetworkOperation.CreateTcp:
                    tcp.Add(network.CreateTcpSocket(endPoint.AddressFamily));
                    break;

                case NetworkOperation.CreateUdp:
                    udp.Add(network.CreateUdpSocket(endPoint.AddressFamily));
                    break;

                case NetworkOperation.Bind when index % 2 == 0 && tcp.Count > 0:
                    var tcpSocket = FuzzInput.Pick(tcp, index);
                    if (tcpSocket.LocalEndPoint is null && tcpSocket.RemoteEndPoint is null)
                    {
                        tcpSocket.Bind(endPoint);
                    }

                    break;

                case NetworkOperation.Bind when udp.Count > 0:
                    var udpSocket = FuzzInput.Pick(udp, index);
                    if (udpSocket.LocalEndPoint is null)
                    {
                        udpSocket.Bind(endPoint);
                    }

                    break;

                case NetworkOperation.Listen when tcp.Count > 0:
                    var listener = FuzzInput.Pick(tcp, index);
                    if (listener.LocalEndPoint is not null && listener.RemoteEndPoint is null && !accepted.Contains(listener))
                    {
                        listener.Listen(16);
                        listeners.Add(listener);
                    }

                    break;

                case NetworkOperation.Connect when tcp.Count > 0:
                    // A connection to a listener of the network: the bytes go from the client to the accepted socket
                    var client = network.CreateTcpSocket(endPoint.AddressFamily);
                    tcp.Add(client);
                    client.ConnectAsync(endPoint, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    var server = listeners.FirstOrDefault(socket => socket.LocalEndPoint is { } local && local.Port == endPoint.Port && local.AddressFamily == endPoint.AddressFamily && (local.Address.Equals(endPoint.Address) || local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any)));
                    FuzzAssert.True(server is not null, "A connection succeeded without a listener on " + endPoint);
                    var connection = server.AcceptAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    tcp.Add(connection);
                    accepted.Add(connection);
                    client.SendAsync(new byte[] { index }, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    var received = new byte[1];
                    FuzzAssert.Equal(1, connection.ReceiveAsync(received, CancellationToken.None).AsTask().GetAwaiter().GetResult(), "The accepted socket receives the byte");
                    FuzzAssert.Equal(index, received[0], "The byte");
                    FuzzAssert.Equal(client.LocalEndPoint, connection.RemoteEndPoint, "The remote address of the accepted socket is the client");
                    break;

                case NetworkOperation.SendDatagram when udp.Count > 0:
                    var sender = FuzzInput.Pick(udp, index);
                    var receiver = udp.Find(socket => socket != sender && socket.LocalEndPoint is { } local && local.Port == endPoint.Port && local.AddressFamily == endPoint.AddressFamily && (local.Address.Equals(endPoint.Address) || local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any)));
                    if (sender.LocalEndPoint is { } senderEndPoint && senderEndPoint.AddressFamily != endPoint.AddressFamily)
                        break;

                    sender.SendToAsync(new byte[] { index }, endPoint, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    if (receiver is not null && (IPAddress.IsLoopback(endPoint.Address) || endPoint.Address.Equals(IPAddress.Any) || endPoint.Address.Equals(IPAddress.IPv6Any)))
                    {
                        // The datagram is delivered when the receiver does not limit the datagrams to another peer: it is read when it is there
                        var buffer = new byte[16];
                        var task = receiver.ReceiveFromAsync(buffer, CancellationToken.None).AsTask();
                        if (task.IsCompleted)
                        {
                            var (length, _) = task.GetAwaiter().GetResult();
                            FuzzAssert.True(length == 1, "A datagram of one byte");
                        }
                    }

                    break;

                case NetworkOperation.Dispose when tcp.Count + udp.Count > 0:
                    if (index % 2 == 0 && tcp.Count > 0)
                    {
                        var disposed = FuzzInput.Pick(tcp, index);
                        disposed.Dispose();
                        tcp.Remove(disposed);
                        listeners.Remove(disposed);
                    }
                    else if (udp.Count > 0)
                    {
                        var disposed = FuzzInput.Pick(udp, index);
                        disposed.Dispose();
                        udp.Remove(disposed);
                    }

                    break;
            }
        }
    }

    /// <summary>Checks that no two sockets are bound to the same port of the same address (or of all the addresses).</summary>
    private static void CheckBindings(IEnumerable<IPEndPoint?> endPoints)
    {
        var bound = endPoints.OfType<IPEndPoint>().ToList();
        for (var i = 0; i < bound.Count; i++)
        {
            for (var j = i + 1; j < bound.Count; j++)
            {
                var (a, b) = (bound[i], bound[j]);
                var any = a.Address.Equals(IPAddress.Any) || a.Address.Equals(IPAddress.IPv6Any) || b.Address.Equals(IPAddress.Any) || b.Address.Equals(IPAddress.IPv6Any);
                FuzzAssert.False(a.AddressFamily == b.AddressFamily && a.Port == b.Port && (any || a.Address.Equals(b.Address)), "Two sockets are bound to " + a + " and " + b);
            }
        }
    }
}
