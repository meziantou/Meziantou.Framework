---
title: Querying a server
description: Read the time, the clock offset and the delay from an NTP response, choose the version and the timeout, and handle errors.
---

## Measure the offset of the local clock

`ClockOffset` is the difference between the clock of the server and the local clock: positive when the server is
ahead. Here the server answers from a clock one hour ahead:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_ClockOffset;format=md-fenced-code-block;dedent=true -->
```csharp
// A server whose clock is one hour ahead of the local clock
var serverClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
using var server = new NtpServer(new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,
    TimeProvider = serverClock,
});
await server.StartAsync();

var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
var response = await client.QueryAsync();

Assert.InRange(response.ClockOffset, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
Assert.Equal(serverClock.GetUtcNow(), response.TransmitTimestamp, TimeSpan.FromSeconds(1));
```
<!-- endref -->

The offset is computed from the four timestamps of the exchange, as NTP defines it: it removes the time the packets
spent on the network, assuming they took as long each way. `RoundTripDelay` is that network time; an asymmetric route
makes the offset wrong by up to half of it, so prefer a server with a short delay.

To get the time of the server from the local clock, add the offset: `DateTimeOffset.UtcNow + response.ClockOffset`.

## The response

| Property | Contains |
| --- | --- |
| `ClockOffset`, `RoundTripDelay` | Computed from the timestamps |
| `OriginateTimestamp` | When the client sent the request, by the local clock |
| `ReceiveTimestamp`, `TransmitTimestamp` | When the server received the request and sent the reply, by its clock |
| `DestinationTimestamp` | When the client received the reply, by the local clock |
| `Stratum` | The distance of the server from a reference clock: 1 for a server with an attached clock (GPS, atomic), 2 and more for servers that get the time from another server |
| `RootDelay`, `RootDispersion` | The delay to the reference clock, and the maximum error the server claims for its own clock. Use `RootDispersion` as the error bound of the offset. |
| `ReferenceIdentifier`, `ReferenceIdentifierText` | The source of the time: a clock name such as `GPS` for stratum 1 (`ReferenceIdentifierText`), an address for higher strata |
| `ReferenceTimestamp` | When the clock of the server was last set, or `null` when it never was |
| `LeapIndicator` | A leap second announcement, or `AlarmCondition` when the clock of the server is not synchronized |
| `Version`, `PollInterval`, `Precision` | The protocol version of the reply, and information about the server clock (in log2 seconds) |
| `IsKissOfDeath`, `KissCode` | A refusal of the server. See [Response validation](04-ResponseValidation.md#kiss-o-death-replies). |

The timestamps are `DateTimeOffset` values in UTC. NTP timestamps have 32 bits of seconds, so the client reads them in
the range from 1968-01-20 to 2104-02-26, following RFC 5905.

## Options

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("127.0.0.1", new NtpClientOptions
{
    Port = port,                         // 123 by default
    Version = NtpVersion.V3,             // V4 by default
    Timeout = TimeSpan.FromSeconds(10),  // per resolved address, 5 seconds by default
});

// Bounds the total duration, whatever the number of addresses the host name resolves to
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var response = await client.QueryAsync(cts.Token);

// The server answers with the version of the request
Assert.Equal(NtpVersion.V3, response.Version);
```
<!-- endref -->

| Option | Default | Use |
| --- | --- | --- |
| `Port` | 123 | The port of the server. |
| `Version` | `V4` | The NTP version of the request: `V3` or `V4`. |
| `Timeout` | 5 seconds | How long to wait for a reply from each address. `Timeout.InfiniteTimeSpan` disables it. |
| `ValidateResponse` | `true` | Rejects the replies that do not answer the request. See [Response validation](04-ResponseValidation.md). |

## Host names with several addresses

A host name is resolved at each `QueryAsync` call. The client queries its addresses in order, and returns the first
valid reply. `Timeout` applies to each address, so a name with several addresses that do not answer can take
`Timeout` times the number of addresses: pass a `CancellationToken` to bound the whole call, as above.

## Errors

`QueryAsync` throws:

| Exception | Cause |
| --- | --- |
| `TimeoutException` | No valid reply within `Timeout` (from every address). |
| `OperationCanceledException` | The `CancellationToken` was canceled. |
| `AggregateException` | Every address failed, for different reasons. |
| `SocketException` | The host name could not be resolved, or the network failed. |
| `InvalidOperationException` | The host name resolved to no address. |

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_Timeout;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("127.0.0.1", new NtpClientOptions
{
    Port = port,
    Timeout = TimeSpan.FromSeconds(1),
});

await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync());
```
<!-- endref -->

The client sends one request per address and does not retry. Public NTP servers rate-limit their clients: in a
long-running process, query a public server every few minutes at most, not in a loop.

## Tracing

The client emits an `Activity` named `ntp.query` for each call, from the `ActivitySource` named
`Meziantou.Framework.NtpClient`, with the tags `ntp.server`, `ntp.version`, `ntp.stratum` and `ntp.address_family`.
