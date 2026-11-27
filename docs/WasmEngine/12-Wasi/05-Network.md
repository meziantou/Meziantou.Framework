---
title: Network
description: Give WASI 0.3 components the TCP and UDP sockets of wasi:sockets, on the network of the machine with rules, or on a network inside the process.
---

The components of WASI 0.3 open TCP and UDP sockets and look up host names with `wasi:sockets`. A component has no
network by default: its sockets fail with `access-denied`. Give it one with
[WasiOptions.Network](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.Network):

- [WasiHostNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetwork) uses the sockets of the machine, with the
  rules of a [WasiHostNetworkOptions](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions).
- [WasiInMemoryNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork) is a network inside the process:
  the components connect to each other and to the host, without the network of the machine.

Both are [WasiNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiNetwork): derive from it to give the components
another network, such as a proxy. WASI checks the states of the sockets and the arguments (a socket that is already
bound, an address of the wrong family, a connection to the port 0...), so a network only performs the operations.

## The network of the machine

The rules of a [WasiHostNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetwork) deny everything by default:
give the component only the access it needs.

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiNetwork_HostNetwork;format=md-fenced-code-block;dedent=true -->
```csharp
// The component can only use the loopback addresses (the services of this machine), and cannot look up names
var network = new WasiHostNetwork(new WasiHostNetworkOptions
{
    AddressCheck = WasiHostNetworkOptions.AllowLoopback,
    AllowNameLookup = false,
    AllowUdp = false,
});

var wasi = new WasiComponentContext(new WasiOptions { Network = network });
```
<!-- endref -->

- [AddressCheck](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AddressCheck) decides each address
  that the component binds to, connects to, or sends a datagram to
  ([WasiSocketAddressUse](cref:Meziantou.Framework.WasmEngine.Wasi.WasiSocketAddressUse)): a denied address fails with
  `access-denied`. `null` denies every address.
  [AllowLoopback](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AllowLoopback) allows the services of
  the machine only, and [AllowAll](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AllowAll) every
  address.
- [AllowNameLookup](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AllowNameLookup) lets the component
  look up host names (`ip-name-lookup`). An IP address given as a name is always returned: it is not looked up.
- [AllowTcp](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AllowTcp) and
  [AllowUdp](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetworkOptions.AllowUdp) disable a protocol.

The IPv6 sockets only use IPv6, as WASI requires. The sockets of the machine are not supported on the browser.

## A network inside the process

A [WasiInMemoryNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork) works on every platform,
including the browser. The host listens and connects next to the components, to test a server written as a
component, or to give a component a service of the host:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiNetwork_InMemoryNetwork;format=md-fenced-code-block;dedent=true -->
```csharp
// A network inside the process: the host listens and connects next to the components, without the network of the machine
var network = new WasiInMemoryNetwork();
using var listener = network.Listen(new IPEndPoint(IPAddress.Loopback, 8080));

await using var client = await network.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8080));
await using var server = await listener.AcceptAsync();
await client.WriteAsync("ping"u8.ToArray());

var buffer = new byte[4];
await server.ReadExactlyAsync(buffer);
Assert.Equal("ping", Encoding.ASCII.GetString(buffer));

var wasi = new WasiComponentContext(new WasiOptions { Network = network });
```
<!-- endref -->

- The addresses of the network are the loopback addresses (`127.0.0.0/8` and `::1`) and the
  [LocalAddresses](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork.LocalAddresses). The other addresses
  are not reachable (`remote-unreachable`), and binding to them fails with `address-not-bindable`.
- The ports are allocated as on a machine: a port 0 is a free port from 49152, and two sockets cannot bind to the same
  port of the same address (`address-in-use`). A connection to a port without listener is refused.
- [HostNames](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork.HostNames) gives the addresses of the names:
  `localhost` is the loopback addresses.
- [ConnectAsync](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork.ConnectAsync) connects the host to a
  socket that a component listens on, and [Listen](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork.Listen)
  accepts the connections of the components: the connections are .NET streams.

## What is supported

- `wasi:sockets/types`: the TCP sockets (bind, connect, listen with the stream of the connections, the streams of the
  data, the options) and the UDP sockets (bind, connect, disconnect, the datagrams, the options).
- `wasi:sockets/ip-name-lookup`.
- WASI 0.3 only: the sockets of WASI 0.2 and of WASI Preview 1 (`sock_*`) are not supported.

A TCP socket and the streams of its data and of its connections share the socket: it is closed when the component
drops all of them. A component that drops a stream can bind to the same address right away.
