---
title: Running a server
description: Start an NTP server, choose its address and its time source, describe the quality of its clock, and limit the request rate.
---

## Start a server

[NtpServer](cref:Meziantou.Framework.Ntp.NtpServer) listens on UDP. [StartAsync](cref:Meziantou.Framework.Ntp.NtpServer.StartAsync) returns once the socket is bound; the server answers in the background until
it is disposed:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Start;format=md-fenced-code-block;dedent=true -->
```csharp
// Start an NTP server on a random port, serving every network interface
using var server = new NtpServer(new NtpServerOptions { Port = 0 });
await server.StartAsync();

Assert.NotEqual(0, server.Port); // The port the operating system assigned
```
<!-- endref -->

- [Port](cref:Meziantou.Framework.Ntp.NtpServerOptions.Port) is 0 by default: the system chooses a free port, and `server.Port` returns it. Set 123, the standard port, to
  serve standard clients; on Linux and macOS, ports below 1024 need elevated privileges.
- [StartAsync](cref:Meziantou.Framework.Ntp.NtpServer.StartAsync) throws when the port cannot be bound. It can be called only once: create another server to start again.
- The server answers with the version of the request (3 or 4), and ignores packets that are not client requests.

## Choose the address

[BindAddress](cref:Meziantou.Framework.Ntp.NtpServerOptions.BindAddress) is `IPAddress.Any` by default: every IPv4 interface. Bind to the loopback address to serve only the local
machine, for instance in tests:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Loopback;format=md-fenced-code-block;dedent=true -->
```csharp
using var server = new NtpServer(new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,
});
await server.StartAsync();
```
<!-- endref -->

To serve IPv6 clients, start a server with `IPAddress.IPv6Any` or `IPAddress.IPv6Loopback`.

## Choose the time source

The server answers with `TimeProvider.GetUtcNow()` of [NtpServerOptions.TimeProvider](cref:Meziantou.Framework.Ntp.NtpServerOptions.TimeProvider), the system clock by default. Any
`TimeProvider` works, such as `FakeTimeProvider` from the `Microsoft.Extensions.TimeProvider.Testing` package, to test
a client at a fixed date:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_CustomTimeProvider;format=md-fenced-code-block;dedent=true -->
```csharp
var myCustomTimeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 01, 01, 00, 00, 00, TimeSpan.Zero));

using var server = new NtpServer(new NtpServerOptions
{
    Port = 0,
    TimeProvider = myCustomTimeProvider,
    Stratum = 2,
});
await server.StartAsync();
```
<!-- endref -->

The time must be in the range of NTP timestamps (1968 to 2104): the server does not answer requests while its clock is
outside of it.

## Describe the quality of the clock

The server does not synchronize its clock: it is as accurate as its `TimeProvider`. The defaults tell clients so:
stratum 2, the reference identifier `LOCL` (an undisciplined local clock), and a root dispersion of 100 ms. Change them
only when they are true:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Options;format=md-fenced-code-block;dedent=true -->
```csharp
using var server = new NtpServer(new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,

    // Only when the machine has a reference clock attached, such as a GPS receiver
    Stratum = 1,
    ReferenceIdentifier = "GPS",
    RootDispersion = TimeSpan.FromMilliseconds(5),

    MaxRequestsPerSecond = 10, // per source address, 0 to disable
});
await server.StartAsync();

var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
var response = await client.QueryAsync();

Assert.Equal(1, response.Stratum);
Assert.Equal("GPS", response.ReferenceIdentifierText);
```
<!-- endref -->

| Option | Default | Use |
| --- | --- | --- |
| [Stratum](cref:Meziantou.Framework.Ntp.NtpServerOptions.Stratum) | 2 | 1 claims a reference clock attached to the machine. Clients prefer lower strata. |
| [ReferenceIdentifier](cref:Meziantou.Framework.Ntp.NtpServerOptions.ReferenceIdentifier) | `LOCL` | Up to four ASCII characters naming the reference clock, such as `GPS` or `PPS`. |
| [RootDispersion](cref:Meziantou.Framework.Ntp.NtpServerOptions.RootDispersion) | 100 ms | The maximum error of the clock. Clients use it as the error bound of the offset; zero claims a perfect clock. |
| [MaxRequestsPerSecond](cref:Meziantou.Framework.Ntp.NtpServerOptions.MaxRequestsPerSecond) | 100 | See [Rate limiting](#rate-limiting). |

## Rate limiting

The server answers at most [MaxRequestsPerSecond](cref:Meziantou.Framework.Ntp.NtpServerOptions.MaxRequestsPerSecond) requests per second from each source address. The first request over
the limit in a second gets a `RATE` Kiss-o'-Death reply, and the following ones are dropped, so the server cannot be
used to send a flood of replies to a spoofed address. Set 0 to disable the limit.

The limit is approximate: source addresses are mapped to a fixed number of buckets, so the memory does not grow with
the number of addresses, and addresses that share a bucket share their budget. It is measured with the real clock of
the machine, not with `TimeProvider`, so a fake clock does not change it.

## Stop the server

Dispose the server to stop it. [Completion](cref:Meziantou.Framework.Ntp.NtpServer.Completion) is a task that completes when the server stops, because it was disposed or
because the token passed to [StartAsync](cref:Meziantou.Framework.Ntp.NtpServer.StartAsync) was canceled, and fails when the listen loop stops on an error:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Completion;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new CancellationTokenSource();
var cancellationToken = cts.Token;

using var server = new NtpServer(new NtpServerOptions { Port = 0 });
await server.StartAsync(cancellationToken);

await cts.CancelAsync();
await server.Completion; // completes on Dispose or cancellation, faults on an unrecoverable error
```
<!-- endref -->

Await or monitor [Completion](cref:Meziantou.Framework.Ntp.NtpServer.Completion) in a long-running host, so a server that stopped answering does not go unnoticed. An
error on a single datagram does not stop the server.

## Tracing

The server emits an `Activity` named `ntp.server.request` for each request, from the `ActivitySource` named
`Meziantou.Framework.NtpServer`, with the tags `ntp.client.address`, `ntp.version` and, for a request over the limit,
`ntp.rate_limited`.
