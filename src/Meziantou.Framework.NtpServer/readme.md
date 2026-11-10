# Meziantou.Framework.NtpServer

An NTP server that responds to NTP time queries. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Mirrors the version sent by the client
- **Configurable time source**: Use `TimeProvider` for testability
- **Configurable binding**: Serve every interface, or just the local machine
- **Rate limiting**: Per-source limiting with `RATE` Kiss-o'-Death replies, enabled by default
- **Symmetric key authentication**: AES-128-CMAC (RFC 8573), SHA-1 and MD5 MACs (RFC 5905)
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

### Authentication

The server authenticates the requests that end with a MAC computed with one of `AuthenticationKeys`, and
authenticates its reply with the same key. A request whose MAC is not valid gets a crypto-NAK, and
`RequireAuthentication` ignores the requests without a MAC:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Authentication;format=md-fenced-code-block;dedent=true -->
```csharp
// The same key on both sides: a line "1 AES128CMAC 000102030405060708090a0b0c0d0e0f" of an ntpd or chrony keys file
var key = new NtpAuthenticationKey(
    keyId: 1,
    NtpAuthenticationAlgorithm.AesCmac,
    Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));

var serverOptions = new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,
    RequireAuthentication = true, // ignore the requests without a valid MAC
};
serverOptions.AuthenticationKeys.Add(key);

using var server = new NtpServer(serverOptions);
await server.StartAsync();

var client = new NtpClient("127.0.0.1", new NtpClientOptions
{
    Port = server.Port,
    AuthenticationKey = key, // ignore the replies without a valid MAC
});
var response = await client.QueryAsync();
```
<!-- endref -->

## What this server does not do

- **It does not discipline its clock.** Time comes from `TimeProvider`, so the server is only as
  accurate as the machine it runs on. That is why the default `Stratum` is 2 with a `LOCL` reference
  identifier and a 100 ms `RootDispersion`: clients are told this is an undisciplined local clock, not
  a primary reference. Set `Stratum = 1` and a matching `ReferenceIdentifier` only if you really have
  attached a reference clock.
- **It does not implement NTS** (RFC 8915). Only clients that share a key with the server can verify that a
  reply came from it.
