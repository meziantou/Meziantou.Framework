using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.WasmEngine.Components;
using static Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentValues;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>
/// The interfaces <c>wasi:sockets/types</c> and <c>wasi:sockets/ip-name-lookup</c> of WASI 0.3: the states of the
/// sockets and the checks of the arguments, over the sockets of a <see cref="WasiNetwork"/>.
/// </summary>
internal sealed class WasiSockets
{
    private const int MaxDatagramSize = 65535;
    private const int BufferSize = 64 * 1024;

    private readonly WasiNetwork? _network;
    private readonly ComponentResourceType _tcpSocket;
    private readonly ComponentResourceType _udpSocket;

    public WasiSockets(WasiNetwork? network)
    {
        _network = network;
        _tcpSocket = new ComponentResourceType("tcp-socket", resource => ((TcpSocket)resource.Representation!).Release());
        _udpSocket = new ComponentResourceType("udp-socket", resource => ((UdpSocket)resource.Representation!).Dispose());
    }

    /// <summary>A function of the host whose arguments are copied: the callbacks capture them.</summary>
    private static ComponentFunction Sync(Func<ComponentCallContext, ComponentValue[], ComponentValue?> body) => Function((context, arguments) => body(context, arguments.ToArray()));

    /// <summary>An error of the operation: a case of <c>error-code</c>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The error is converted to an error code of WASI: it does not leave the assembly")]
    private sealed class SocketsException : Exception
    {
        public SocketsException()
            : this("other")
        {
        }

        public SocketsException(string code)
            : base(code)
        {
            Code = code;
        }

        public SocketsException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "other";
        }

        public string Code { get; }
    }

    private static SocketsException InvalidState() => new("invalid-state");

    private static SocketsException InvalidArgument() => new("invalid-argument");

    private WasiNetwork Network => _network ?? throw new SocketsException("access-denied");

    public void Define(ComponentImports imports, Func<string, string> name)
    {
        var types = new ComponentImports()
            .Define("tcp-socket", _tcpSocket)
            .Define("udp-socket", _udpSocket);
        DefineTcp(types);
        DefineUdp(types);
        imports.Define(name("wasi:sockets/types"), types);
        imports.Define(name("wasi:sockets/ip-name-lookup"), new ComponentImports()
            .Define("resolve-addresses", AsyncFunction(async (context, arguments) => await ResolveAddressesAsync(arguments[0].AsString(), context.CancellationToken).ConfigureAwait(true))));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Errors and addresses
    // ------------------------------------------------------------------------------------------------------------

    private static string GetErrorCode(SocketError error) => error switch
    {
        SocketError.AccessDenied => "access-denied",
        SocketError.AddressFamilyNotSupported or SocketError.ProtocolFamilyNotSupported or SocketError.ProtocolNotSupported or SocketError.SocketNotSupported or SocketError.OperationNotSupported or SocketError.ProtocolOption => "not-supported",
        SocketError.InvalidArgument or SocketError.DestinationAddressRequired or SocketError.Fault => "invalid-argument",
        SocketError.NoBufferSpaceAvailable => "out-of-memory",
        SocketError.TimedOut => "timeout",
        SocketError.IsConnected or SocketError.NotConnected or SocketError.AlreadyInProgress or SocketError.InProgress => "invalid-state",
        SocketError.AddressNotAvailable => "address-not-bindable",
        SocketError.AddressAlreadyInUse => "address-in-use",
        SocketError.HostUnreachable or SocketError.HostDown or SocketError.NetworkUnreachable or SocketError.NetworkDown => "remote-unreachable",
        SocketError.ConnectionRefused => "connection-refused",
        SocketError.Shutdown => "connection-broken",
        SocketError.ConnectionReset or SocketError.NetworkReset => "connection-reset",
        SocketError.ConnectionAborted or SocketError.OperationAborted => "connection-aborted",
        SocketError.MessageSize => "datagram-too-large",
        _ => "",
    };

    private static ComponentValue ErrorCode(Exception exception)
    {
        var code = exception switch
        {
            SocketsException sockets => sockets.Code,
            SocketException socket => GetErrorCode(socket.SocketErrorCode),
            ObjectDisposedException => "invalid-state",
            _ => "",
        };

        return code.Length == 0 ? ComponentValue.FromCase("other", ComponentValue.Some(String(exception.Message))) : ComponentValue.FromCase(code);
    }

    private static bool IsError(Exception exception) => exception is SocketsException or SocketException or ObjectDisposedException or IOException;

    private static ComponentValue Result(Func<ComponentValue?> operation)
    {
        try
        {
            return ComponentValue.Ok(operation());
        }
        catch (Exception ex) when (IsError(ex))
        {
            return ComponentValue.Error(ErrorCode(ex));
        }
    }

    private static ComponentValue Unit(Action operation) => Result(() =>
    {
        operation();
        return null;
    });

    private static AddressFamily GetFamily(ComponentValue value) => value.IsCase("ipv6") ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

    private static ComponentValue Family(AddressFamily family) => ComponentValue.FromEnum(family == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4");

    private static IPAddress ToAddress(ComponentValue value, bool isV6)
    {
        var parts = value.AsRecord();
        if (!isV6)
            return new IPAddress([parts[0].AsU8(), parts[1].AsU8(), parts[2].AsU8(), parts[3].AsU8()]);

        var bytes = new byte[16];
        for (var i = 0; i < 8; i++)
        {
            var part = parts[i].AsU16();
            bytes[2 * i] = (byte)(part >> 8);
            bytes[(2 * i) + 1] = (byte)part;
        }

        return new IPAddress(bytes);
    }

    private static ComponentValue FromAddressParts(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return ComponentValue.FromTuple(ComponentValue.FromU8(bytes[0]), ComponentValue.FromU8(bytes[1]), ComponentValue.FromU8(bytes[2]), ComponentValue.FromU8(bytes[3]));

        var parts = new ComponentValue[8];
        for (var i = 0; i < 8; i++)
        {
            parts[i] = ComponentValue.FromU16((ushort)((bytes[2 * i] << 8) | bytes[(2 * i) + 1]));
        }

        return ComponentValue.FromTuple(parts);
    }

    /// <summary>Converts an <c>ip-socket-address</c>.</summary>
    internal static IPEndPoint ToEndPoint(ComponentValue value)
    {
        var record = value.Payload!.Value.AsRecord();
        if (value.IsCase("ipv4"))
            return new IPEndPoint(ToAddress(record[1], isV6: false), record[0].AsU16());

        var address = ToAddress(record[2], isV6: true);
        address.ScopeId = record[3].AsU32();
        return new IPEndPoint(address, record[0].AsU16());
    }

    /// <summary>Converts to an <c>ip-socket-address</c>.</summary>
    internal static ComponentValue FromEndPoint(IPEndPoint endPoint)
    {
        var port = ComponentValue.FromU16((ushort)endPoint.Port);
        if (endPoint.AddressFamily == AddressFamily.InterNetwork)
            return ComponentValue.FromCase("ipv4", ComponentValue.FromRecord(port, FromAddressParts(endPoint.Address)));

        return ComponentValue.FromCase("ipv6", ComponentValue.FromRecord(port, ComponentValue.FromU32(0), FromAddressParts(endPoint.Address), ComponentValue.FromU32((uint)endPoint.Address.ScopeId)));
    }

    private static ComponentValue FromAddress(IPAddress address) => ComponentValue.FromCase(address.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4", FromAddressParts(address));

    private static bool IsDottedQuad(string name)
    {
        var parts = name.Split('.');
        return parts.Length == 4 && parts.All(part => part.Length is > 0 and <= 3 && part.All(char.IsAsciiDigit));
    }

    private static bool IsUnspecified(IPAddress address) => address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    private static bool IsMulticastOrBroadcast(IPAddress address) => address.AddressFamily == AddressFamily.InterNetworkV6
        ? address.IsIPv6Multicast
        : address.GetAddressBytes() is [>= 224 and <= 239, ..] or [255, 255, 255, 255];

    /// <summary>Checks an address of a socket: its family, and a unicast address that is not an IPv4-mapped IPv6 address.</summary>
    private static void CheckAddress(IPEndPoint endPoint, AddressFamily family, bool remote, bool unicast)
    {
        if (endPoint.AddressFamily != family)
            throw InvalidArgument();

        if (endPoint.Address.IsIPv4MappedToIPv6)
            throw InvalidArgument();

        if (unicast && IsMulticastOrBroadcast(endPoint.Address))
            throw InvalidArgument();

        if (remote && (IsUnspecified(endPoint.Address) || endPoint.Port == 0))
            throw InvalidArgument();
    }

    private static int ToInt32(ulong value) => value == 0 ? throw InvalidArgument() : (int)Math.Min(value, int.MaxValue);

    // A duration shorter than a tick is a tick: the durations are rounded, but never to 0
    private static TimeSpan ToDuration(ulong nanoseconds) => nanoseconds == 0 ? throw InvalidArgument() : TimeSpan.FromTicks((long)Math.Clamp(nanoseconds / 100, 1, (ulong)TimeSpan.MaxValue.Ticks));

    private static ComponentValue FromDuration(TimeSpan value) => ComponentValue.FromU64((ulong)Math.Max(value.Ticks, 0) * 100);

    // ------------------------------------------------------------------------------------------------------------
    // ip-name-lookup
    // ------------------------------------------------------------------------------------------------------------

    private async ValueTask<ComponentValue?> ResolveAddressesAsync(string name, CancellationToken cancellationToken)
    {
        // The IPv4 addresses are the dotted quads: IPAddress.TryParse accepts the legacy forms, such as 10 for 0.0.0.10
        if (IPAddress.TryParse(name, out var literal) && (literal.AddressFamily == AddressFamily.InterNetworkV6 ? name.Contains(':', StringComparison.Ordinal) : IsDottedQuad(name)))
            return ComponentValue.Ok(ComponentValue.FromList([FromAddress(literal.IsIPv4MappedToIPv6 ? literal.MapToIPv4() : literal)]));

        if (name.Length == 0 || Uri.CheckHostName(name) != UriHostNameType.Dns)
            return ComponentValue.Error(ComponentValue.FromCase("invalid-argument"));

        if (_network is null)
            return ComponentValue.Error(ComponentValue.FromCase("access-denied"));

        try
        {
            var addresses = await _network.ResolveAddressesAsync(name, cancellationToken).ConfigureAwait(true);
            var results = addresses.Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).Distinct().Select(FromAddress).ToArray();
            return results.Length == 0 ? ComponentValue.Error(ComponentValue.FromCase("name-unresolvable")) : ComponentValue.Ok(ComponentValue.FromList(results));
        }
        catch (SocketException ex)
        {
            var code = ex.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData => "name-unresolvable",
                SocketError.TryAgain => "temporary-resolver-failure",
                SocketError.NoRecovery => "permanent-resolver-failure",
                SocketError.AccessDenied => "access-denied",
                SocketError.InvalidArgument => "invalid-argument",
                _ => "",
            };

            return ComponentValue.Error(code.Length == 0 ? ComponentValue.FromCase("other", ComponentValue.Some(String(ex.Message))) : ComponentValue.FromCase(code));
        }
        catch (ArgumentException)
        {
            return ComponentValue.Error(ComponentValue.FromCase("invalid-argument"));
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // TCP
    // ------------------------------------------------------------------------------------------------------------

    private enum TcpState
    {
        Unbound,
        Bound,
        Listening,
        Connecting,
        Connected,
        Closed,
    }

    /// <summary>
    /// A TCP socket: the handle of the component and the streams of its data and of its connections share the socket,
    /// which is closed when the last of them is dropped.
    /// </summary>
    private sealed class TcpSocket(WasiTcpSocket socket, AddressFamily family, TcpState state)
    {
        private int _references = 1;

        public WasiTcpSocket Socket { get; } = socket;

        public AddressFamily Family { get; } = family;

        public TcpState State { get; set; } = state;

        public bool IsSending { get; set; }

        public bool IsReceiving { get; set; }

        public void AddReference() => _references++;

        /// <summary>
        /// Adds a reference that is released once, when the stream that holds it ends or when the component drops it: a
        /// component that drops the stream can bind to the same address right away.
        /// </summary>
        public Action AddStreamReference(CancellationToken readerDropped)
        {
            AddReference();
            var released = false;
            void ReleaseOnce()
            {
                if (!released)
                {
                    released = true;
                    Release();
                }
            }

            readerDropped.Register(ReleaseOnce);
            return ReleaseOnce;
        }

        public void Release()
        {
            if (--_references == 0)
            {
                State = TcpState.Closed;
                Socket.Dispose();
            }
        }
    }

    private static TcpSocket Tcp(ComponentValue value) => Get<TcpSocket>(value);

    private static TcpSocket GetOpenTcp(ComponentValue value)
    {
        var socket = Tcp(value);
        if (socket.State == TcpState.Closed)
            throw InvalidState();

        return socket;
    }

    private void DefineTcp(ComponentImports types)
    {
        types
            .Define("[static]tcp-socket.create", Sync((context, arguments) => Result(() =>
            {
                var family = GetFamily(arguments[0]);
                return Resource(_tcpSocket, new TcpSocket(Network.CreateTcpSocket(family), family, TcpState.Unbound));
            })))
            .Define("[method]tcp-socket.bind", Sync((context, arguments) => Unit(() =>
            {
                var socket = GetOpenTcp(arguments[0]);
                var endPoint = ToEndPoint(arguments[1]);
                CheckAddress(endPoint, socket.Family, remote: false, unicast: true);
                if (socket.State != TcpState.Unbound)
                    throw InvalidState();

                socket.Socket.Bind(endPoint);
                socket.State = TcpState.Bound;
            })))
            .Define("[method]tcp-socket.connect", AsyncFunction(async (context, arguments) => await ConnectAsync(Tcp(arguments[0]), ToEndPoint(arguments[1]), context.CancellationToken).ConfigureAwait(true)))
            .Define("[method]tcp-socket.listen", Sync((context, arguments) => Result(() => Listen(context, GetOpenTcp(arguments[0])))))
            .Define("[method]tcp-socket.send", Sync((context, arguments) => Send(context, Tcp(arguments[0]), arguments[1])))
            .Define("[method]tcp-socket.receive", Sync((context, arguments) => Receive(context, Tcp(arguments[0]))))
            .Define("[method]tcp-socket.get-local-address", Sync((context, arguments) => Result(() =>
            {
                var socket = GetOpenTcp(arguments[0]);
                return socket.State == TcpState.Unbound || socket.Socket.LocalEndPoint is not { } endPoint ? throw InvalidState() : FromEndPoint(endPoint);
            })))
            .Define("[method]tcp-socket.get-remote-address", Sync((context, arguments) => Result(() =>
            {
                var socket = GetOpenTcp(arguments[0]);
                return socket.State != TcpState.Connected || socket.Socket.RemoteEndPoint is not { } endPoint ? throw InvalidState() : FromEndPoint(endPoint);
            })))
            .Define("[method]tcp-socket.get-is-listening", Sync((context, arguments) => ComponentValue.FromBool(Tcp(arguments[0]).State == TcpState.Listening)))
            .Define("[method]tcp-socket.get-address-family", Sync((context, arguments) => Family(Tcp(arguments[0]).Family)))
            .Define("[method]tcp-socket.set-listen-backlog-size", Sync((context, arguments) => Unit(() =>
            {
                var socket = GetOpenTcp(arguments[0]);
                var value = ToInt32(arguments[1].AsU64());
                if (socket.State is TcpState.Connecting or TcpState.Connected)
                    throw InvalidState();

                socket.Socket.ListenBacklog = value;
            })))
            .Define("[method]tcp-socket.get-keep-alive-enabled", Sync((context, arguments) => Result(() => ComponentValue.FromBool(GetOpenTcp(arguments[0]).Socket.KeepAlive))))
            .Define("[method]tcp-socket.set-keep-alive-enabled", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.KeepAlive = arguments[1].AsBool())))
            .Define("[method]tcp-socket.get-keep-alive-idle-time", Sync((context, arguments) => Result(() => FromDuration(GetOpenTcp(arguments[0]).Socket.KeepAliveIdleTime))))
            .Define("[method]tcp-socket.set-keep-alive-idle-time", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.KeepAliveIdleTime = ToDuration(arguments[1].AsU64()))))
            .Define("[method]tcp-socket.get-keep-alive-interval", Sync((context, arguments) => Result(() => FromDuration(GetOpenTcp(arguments[0]).Socket.KeepAliveInterval))))
            .Define("[method]tcp-socket.set-keep-alive-interval", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.KeepAliveInterval = ToDuration(arguments[1].AsU64()))))
            .Define("[method]tcp-socket.get-keep-alive-count", Sync((context, arguments) => Result(() => ComponentValue.FromU32((uint)Math.Max(GetOpenTcp(arguments[0]).Socket.KeepAliveCount, 0)))))
            .Define("[method]tcp-socket.set-keep-alive-count", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.KeepAliveCount = ToInt32(arguments[1].AsU32()))))
            .Define("[method]tcp-socket.get-hop-limit", Sync((context, arguments) => Result(() => ComponentValue.FromU8((byte)Math.Clamp(GetOpenTcp(arguments[0]).Socket.HopLimit, 0, 255)))))
            .Define("[method]tcp-socket.set-hop-limit", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.HopLimit = ToInt32(arguments[1].AsU8()))))
            .Define("[method]tcp-socket.get-receive-buffer-size", Sync((context, arguments) => Result(() => ComponentValue.FromU64((ulong)Math.Max(GetOpenTcp(arguments[0]).Socket.ReceiveBufferSize, 0)))))
            .Define("[method]tcp-socket.set-receive-buffer-size", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.ReceiveBufferSize = ToInt32(arguments[1].AsU64()))))
            .Define("[method]tcp-socket.get-send-buffer-size", Sync((context, arguments) => Result(() => ComponentValue.FromU64((ulong)Math.Max(GetOpenTcp(arguments[0]).Socket.SendBufferSize, 0)))))
            .Define("[method]tcp-socket.set-send-buffer-size", Sync((context, arguments) => Unit(() => GetOpenTcp(arguments[0]).Socket.SendBufferSize = ToInt32(arguments[1].AsU64()))));
    }

    private static async ValueTask<ComponentValue?> ConnectAsync(TcpSocket socket, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        try
        {
            CheckAddress(remoteEndPoint, socket.Family, remote: true, unicast: true);
            if (socket.State is not (TcpState.Unbound or TcpState.Bound))
                throw InvalidState();
        }
        catch (SocketsException ex)
        {
            return ComponentValue.Error(ErrorCode(ex));
        }

        // A failed connection closes the socket: it cannot connect again
        socket.State = TcpState.Connecting;
        try
        {
            await socket.Socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(true);
            if (socket.State == TcpState.Connecting)
            {
                socket.State = TcpState.Connected;
            }

            return ComponentValue.Ok();
        }
        catch (Exception ex) when (IsError(ex))
        {
            socket.State = TcpState.Closed;
            return ComponentValue.Error(ErrorCode(ex));
        }
        catch (OperationCanceledException)
        {
            socket.State = TcpState.Closed;
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The background loop disposes the writer")]
    private ComponentValue Listen(ComponentCallContext context, TcpSocket socket)
    {
        if (socket.State is not (TcpState.Unbound or TcpState.Bound))
            throw InvalidState();

        if (socket.State == TcpState.Unbound)
        {
            // Some systems (Windows) do not bind a listening socket implicitly
            socket.Socket.Bind(new IPEndPoint(socket.Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            socket.State = TcpState.Bound;
        }

        socket.Socket.Listen(socket.Socket.ListenBacklog);
        socket.State = TcpState.Listening;
        var elementType = context.Type.Result is ComponentResultType { Ok: ComponentStreamType stream } ? stream.ElementType : null;
        var writer = ComponentStreamWriter.Create(elementType, out var readable);
        var release = socket.AddStreamReference(writer.ReaderDroppedToken);
        context.RunInBackground(async () =>
        {
            try
            {
                while (!writer.IsReaderDropped)
                {
                    WasiTcpSocket accepted;
                    try
                    {
                        accepted = await socket.Socket.AcceptAsync(writer.ReaderDroppedToken).ConfigureAwait(true);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.TimedOut)
                    {
                        // A connection that ended before it was accepted: the stream of the connections continues. The
                        // other errors (no more sockets, a closed socket...) end it, instead of retrying in a loop
                        continue;
                    }

                    var value = Resource(_tcpSocket, new TcpSocket(accepted, socket.Family, TcpState.Connected));
                    if (await writer.WriteAsync([value]).ConfigureAwait(true) == 0)
                    {
                        accepted.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The component dropped the stream, or the socket is closed
            }
            finally
            {
                writer.Dispose();
                release();
            }
        });

        return readable;
    }

    private static ComponentValueType? GetFutureType(ComponentFunctionType type) => type.Result is ComponentFutureType future ? future.ElementType : null;

    private static ComponentValue Send(ComponentCallContext context, TcpSocket socket, ComponentValue data)
    {
        var reader = data.AsStreamReader();
        var result = ComponentFutureWriter.Create(GetFutureType(context.Type), out var future);
        if (socket.State != TcpState.Connected || socket.IsSending)
        {
            reader.Dispose();
            result.Write(ComponentValue.Error(ErrorCode(InvalidState())));
            return future;
        }

        socket.IsSending = true;
        socket.AddReference();
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            try
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await reader.ReadAsync(buffer).ConfigureAwait(true);
                    if (read == 0 && reader.IsCompleted)
                        break;

                    if (read > 0)
                    {
                        await socket.Socket.SendAsync(buffer.AsMemory(0, read), CancellationToken.None).ConfigureAwait(true);
                    }
                }

                // The end of the stream is the end of the data: the peer receives a FIN
                socket.Socket.ShutdownSend();
                status = ComponentValue.Ok();
            }
            catch (Exception ex) when (IsError(ex))
            {
                status = ComponentValue.Error(ErrorCode(ex));
            }
            finally
            {
                reader.Dispose();
                socket.Release();
            }

            result.Write(status);
        });

        return future;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The background copy disposes the writer")]
    private static ComponentValue Receive(ComponentCallContext context, TcpSocket socket)
    {
        var (streamType, futureType) = context.Type.Result is ComponentTupleType { Types: [ComponentStreamType stream, ComponentFutureType future] } ? (stream.ElementType, future.ElementType) : (null, null);
        var writer = ComponentStreamWriter.Create(streamType, out var readable);
        var result = ComponentFutureWriter.Create(futureType, out var futureValue);
        if (socket.State != TcpState.Connected || socket.IsReceiving)
        {
            writer.Dispose();
            result.Write(ComponentValue.Error(ErrorCode(InvalidState())));
            return ComponentValue.FromTuple(readable, futureValue);
        }

        socket.IsReceiving = true;
        var release = socket.AddStreamReference(writer.ReaderDroppedToken);
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            try
            {
                var buffer = new byte[BufferSize];
                while (!writer.IsReaderDropped)
                {
                    var read = await socket.Socket.ReceiveAsync(buffer, writer.ReaderDroppedToken).ConfigureAwait(true);
                    if (read == 0)
                        break;

                    await writer.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
                }

                status = ComponentValue.Ok();
            }
            catch (OperationCanceledException)
            {
                // The component dropped the stream: it does not receive the rest of the data
                status = ComponentValue.Ok();
            }
            catch (Exception ex) when (IsError(ex))
            {
                status = ComponentValue.Error(ErrorCode(ex));
            }
            finally
            {
                writer.Dispose();
                release();
            }

            result.Write(status);
        });

        return ComponentValue.FromTuple(readable, futureValue);
    }

    // ------------------------------------------------------------------------------------------------------------
    // UDP
    // ------------------------------------------------------------------------------------------------------------

    private sealed class UdpSocket(WasiUdpSocket socket, AddressFamily family) : IDisposable
    {
        public WasiUdpSocket Socket { get; } = socket;

        public AddressFamily Family { get; } = family;

        public bool IsBound { get; set; }

        public IPEndPoint? RemoteEndPoint { get; set; }

        public void Dispose() => Socket.Dispose();
    }

    private static UdpSocket Udp(ComponentValue value) => Get<UdpSocket>(value);

    private void DefineUdp(ComponentImports types)
    {
        types
            .Define("[static]udp-socket.create", Sync((context, arguments) => Result(() =>
            {
                var family = GetFamily(arguments[0]);
                return Resource(_udpSocket, new UdpSocket(Network.CreateUdpSocket(family), family));
            })))
            .Define("[method]udp-socket.bind", Sync((context, arguments) => Unit(() =>
            {
                var socket = Udp(arguments[0]);
                var endPoint = ToEndPoint(arguments[1]);
                CheckAddress(endPoint, socket.Family, remote: false, unicast: false);
                if (socket.IsBound)
                    throw InvalidState();

                socket.Socket.Bind(endPoint);
                socket.IsBound = true;
            })))
            .Define("[method]udp-socket.connect", Sync((context, arguments) => Unit(() =>
            {
                var socket = Udp(arguments[0]);
                var endPoint = ToEndPoint(arguments[1]);
                CheckAddress(endPoint, socket.Family, remote: true, unicast: false);

                // The network can disconnect the socket before the connection fails: the state follows the socket
                try
                {
                    socket.Socket.Connect(endPoint);
                    socket.RemoteEndPoint = endPoint;
                }
                catch
                {
                    socket.RemoteEndPoint = null;
                    throw;
                }
                finally
                {
                    socket.IsBound = socket.Socket.LocalEndPoint is not null;
                }
            })))
            .Define("[method]udp-socket.disconnect", Sync((context, arguments) => Unit(() =>
            {
                var socket = Udp(arguments[0]);
                if (socket.RemoteEndPoint is null)
                    throw InvalidState();

                socket.RemoteEndPoint = null;
                try
                {
                    socket.Socket.Connect(null);
                }
                finally
                {
                    socket.IsBound = socket.Socket.LocalEndPoint is not null;
                }
            })))
            .Define("[method]udp-socket.send", AsyncFunction(async (context, arguments) => await SendDatagramAsync(Udp(arguments[0]), arguments[1].AsBytes(), arguments[2], context.CancellationToken).ConfigureAwait(true)))
            .Define("[method]udp-socket.receive", AsyncFunction(async (context, arguments) => await ReceiveDatagramAsync(Udp(arguments[0]), context.CancellationToken).ConfigureAwait(true)))
            .Define("[method]udp-socket.get-local-address", Sync((context, arguments) => Result(() =>
            {
                var socket = Udp(arguments[0]);
                return !socket.IsBound || socket.Socket.LocalEndPoint is not { } endPoint ? throw InvalidState() : FromEndPoint(endPoint);
            })))
            .Define("[method]udp-socket.get-remote-address", Sync((context, arguments) => Result(() => Udp(arguments[0]).RemoteEndPoint is { } endPoint ? FromEndPoint(endPoint) : throw InvalidState())))
            .Define("[method]udp-socket.get-address-family", Sync((context, arguments) => Family(Udp(arguments[0]).Family)))
            .Define("[method]udp-socket.get-unicast-hop-limit", Sync((context, arguments) => Result(() => ComponentValue.FromU8((byte)Math.Clamp(Udp(arguments[0]).Socket.HopLimit, 0, 255)))))
            .Define("[method]udp-socket.set-unicast-hop-limit", Sync((context, arguments) => Unit(() => Udp(arguments[0]).Socket.HopLimit = ToInt32(arguments[1].AsU8()))))
            .Define("[method]udp-socket.get-receive-buffer-size", Sync((context, arguments) => Result(() => ComponentValue.FromU64((ulong)Math.Max(Udp(arguments[0]).Socket.ReceiveBufferSize, 0)))))
            .Define("[method]udp-socket.set-receive-buffer-size", Sync((context, arguments) => Unit(() => Udp(arguments[0]).Socket.ReceiveBufferSize = ToInt32(arguments[1].AsU64()))))
            .Define("[method]udp-socket.get-send-buffer-size", Sync((context, arguments) => Result(() => ComponentValue.FromU64((ulong)Math.Max(Udp(arguments[0]).Socket.SendBufferSize, 0)))))
            .Define("[method]udp-socket.set-send-buffer-size", Sync((context, arguments) => Unit(() => Udp(arguments[0]).Socket.SendBufferSize = ToInt32(arguments[1].AsU64()))));
    }

    private static async ValueTask<ComponentValue?> SendDatagramAsync(UdpSocket socket, byte[] data, ComponentValue remoteAddress, CancellationToken cancellationToken)
    {
        try
        {
            IPEndPoint destination;
            if (remoteAddress.IsCase("some"))
            {
                destination = ToEndPoint(remoteAddress.Payload!.Value);
                CheckAddress(destination, socket.Family, remote: true, unicast: false);
                if (socket.RemoteEndPoint is { } connected && !connected.Equals(destination))
                    throw InvalidArgument();
            }
            else
            {
                destination = socket.RemoteEndPoint ?? throw InvalidArgument();
            }

            if (data.Length > MaxDatagramSize)
                throw new SocketsException("datagram-too-large");

            await socket.Socket.SendToAsync(data, destination, cancellationToken).ConfigureAwait(true);
            socket.IsBound = true;
            return ComponentValue.Ok();
        }
        catch (Exception ex) when (IsError(ex))
        {
            return ComponentValue.Error(ErrorCode(ex));
        }
    }

    private static async ValueTask<ComponentValue?> ReceiveDatagramAsync(UdpSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            if (!socket.IsBound)
                throw InvalidState();

            var buffer = new byte[MaxDatagramSize];
            var (length, source) = await socket.Socket.ReceiveFromAsync(buffer, cancellationToken).ConfigureAwait(true);
            return ComponentValue.Ok(ComponentValue.FromTuple(ComponentValue.FromBytes(buffer.AsSpan(0, length)), FromEndPoint(source)));
        }
        catch (Exception ex) when (IsError(ex))
        {
            return ComponentValue.Error(ErrorCode(ex));
        }
    }
}
