# Meziantou.Framework.NtpServer

An NTP server that responds to NTP time queries. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Mirrors the version sent by the client
- **Configurable time source**: Use `TimeProvider` for testability
- **Configurable binding**: Serve every interface, or just the local machine
- **Rate limiting**: Per-source limiting with `RATE` Kiss-o'-Death replies, enabled by default
- **Auto-assigned port**: Use port 0 for testing to get an auto-assigned port
- **OpenTelemetry**: Built-in `ActivitySource` tracing for NTP requests

## Usage

The types are in the `Meziantou.Framework.Ntp` namespace.

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Start;format=md-fenced-code-block;dedent=true -->
```csharp
// Start an NTP server on a random port, serving every network interface
using var server = new NtpServer(new NtpServerOptions { Port = 0 });
await server.StartAsync();

Assert.NotEqual(0, server.Port); // The port the operating system assigned
```
<!-- endref -->

### Serving only the local machine

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

### Custom time source

Any `TimeProvider` can be the time source, such as the `FakeTimeProvider` of the `Microsoft.Extensions.TimeProvider.Testing` package:

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

### Observing the listener

`StartAsync` returns once the socket is bound; the listen loop runs in the background. `Completion`
lets you observe it, so a loop that stops is not silent:

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

Per-datagram socket errors do not stop the loop.

### Rate limiting

`MaxRequestsPerSecond` (100 by default, 0 to disable) caps how many requests are answered per source
address. The first request over the limit in a window gets a `RATE` Kiss-o'-Death reply and the rest
are dropped, so answering a throttled source cannot itself be used to reflect traffic.

The limit is approximate: source addresses are mapped onto a fixed number of buckets so that memory
cannot grow with the number of distinct — and easily spoofed — source addresses seen, and colliding
addresses share a budget. It is measured against the real system clock, not `TimeProvider`, so a
simulated clock cannot switch it off.

## What this server does not do

- **It does not discipline its clock.** Time comes from `TimeProvider`, so the server is only as
  accurate as the machine it runs on. That is why the default `Stratum` is 2 with a `LOCL` reference
  identifier and a 100 ms `RootDispersion`: clients are told this is an undisciplined local clock, not
  a primary reference. Set `Stratum = 1` and a matching `ReferenceIdentifier` only if you really have
  attached a reference clock.
- **It does not authenticate anything.** Neither NTS (RFC 8915) nor symmetric key authentication is
  implemented, so any client can query it and nothing proves to a client that a reply came from it.
