# Meziantou.Framework.NtpClient

An NTP client library for querying NTP servers to retrieve accurate network time. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Configurable protocol version
- **Clock offset calculation**: Computes the time difference between client and server
- **Round-trip delay**: Measures network round-trip time
- **Response validation**: Rejects replies that do not answer the request that was actually sent
- **Retries**: Sends a new request when a request or its reply is lost
- **Multi-sample and multi-server measurement**: Clock filter and selection algorithms of RFC 5905
- **Symmetric key authentication**: AES-128-CMAC (RFC 8573), SHA-1 and MD5 MACs (RFC 5905)
- **Async/await**: Fully asynchronous API with cancellation support
- **OpenTelemetry**: Built-in `ActivitySource` tracing for NTP queries

## Usage

The types are in the `Meziantou.Framework.Ntp` namespace.

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_Query;format=md-fenced-code-block;dedent=true -->
```csharp
// Query an NTP server using NTPv4 (default)
var client = new NtpClient("pool.ntp.org");
var response = await client.QueryAsync();

Console.WriteLine($"Server time: {response.TransmitTimestamp}");
Console.WriteLine($"Clock offset: {response.ClockOffset}");
Console.WriteLine($"Round-trip delay: {response.RoundTripDelay}");
Console.WriteLine($"Stratum: {response.Stratum}");

// How much the server itself vouches for that reading
Console.WriteLine($"Root dispersion: {response.RootDispersion}");
```
<!-- endref -->

### Using NTPv3

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_V3;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("pool.ntp.org", new NtpClientOptions
{
    Version = NtpVersion.V3,
});
var response = await client.QueryAsync();
```
<!-- endref -->

### Cancellation and timeout

`NtpClientOptions.Timeout` applies to each resolved address separately, so a host name that resolves
to several addresses can take up to `Timeout × addressCount` overall. Use the cancellation token to
bound the total duration.

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var client = new NtpClient("time.google.com");
var response = await client.QueryAsync(cts.Token);
```
<!-- endref -->

`QueryAsync` throws `TimeoutException` when a query times out, `OperationCanceledException` when the
caller's token is cancelled, and `AggregateException` when several resolved addresses failed for
differing reasons.

### Custom port

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_CustomPort;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("localhost", new NtpClientOptions
{
    Port = 12345,
});
var response = await client.QueryAsync();
```
<!-- endref -->

### Retries

`NtpClientOptions.RetryCount` sends a new request when no valid reply arrives within the timeout:

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_ReadmeRetry;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("pool.ntp.org", new NtpClientOptions
{
    Timeout = TimeSpan.FromSeconds(2),
    RetryCount = 2, // up to 3 requests per address
});
var response = await client.QueryAsync();
```
<!-- endref -->

### Several samples and several servers

One query is one measurement. `MeasureAsync` queries a server several times and keeps the sample with
the shortest round-trip delay, the most accurate. The static `NtpClient.MeasureAsync` measures several
servers, discards the ones whose time disagrees with the majority (the selection algorithm of RFC 5905),
and averages the others:

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_ReadmeMeasureServers;format=md-fenced-code-block;dedent=true -->
```csharp
// A single server, 4 samples 2 seconds apart
var measurement = await new NtpClient("time.google.com").MeasureAsync();
Console.WriteLine($"Offset: {measurement.ClockOffset} ± {measurement.RootDistance}");

// Several servers: the majority outvotes a server whose clock is wrong
var result = await NtpClient.MeasureAsync(
[
    new NtpClient("time.google.com"),
    new NtpClient("time.cloudflare.com"),
    new NtpClient("pool.ntp.org"),
]);

Console.WriteLine($"Offset: {result.ClockOffset}, between {result.MinimumClockOffset} and {result.MaximumClockOffset}");
foreach (var rejected in result.RejectedServers)
{
    Console.WriteLine($"Ignored {rejected.Server}: offset {rejected.ClockOffset}");
}
```
<!-- endref -->

It throws `NtpClockSelectionException` when no majority of the servers that answered agrees.

## Response validation

The client's socket is connected to the server, so the operating system drops datagrams from any other
source. On top of that, `NtpClientOptions.ValidateResponse` (enabled by default) requires that a reply:

- echoes the request's transmit timestamp in its originate timestamp (RFC 5905 TEST2), byte for byte;
- uses version 3 or 4, and server mode;
- is not a Kiss-o'-Death packet (stratum 0);
- does not report an alarm condition, which means the server's own clock is unsynchronized.

A reply that fails is ignored and the client keeps waiting for a valid one until the timeout, so a
single stray or forged datagram cannot deny service.

You can inspect a refusal by turning validation off:

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_WithoutValidation;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("pool.ntp.org", new NtpClientOptions { ValidateResponse = false });
var response = await client.QueryAsync();
if (response.IsKissOfDeath)
{
    Console.WriteLine($"The server refused to answer: {response.KissCode}"); // RATE, DENY, RSTR, ...
}
```
<!-- endref -->

> [!WARNING]
> Validation ties a reply to the request that was sent; it does not authenticate the server. Unless the
> client and the server share a key, an attacker who can inject packets on the path to the server can
> still control the reported time. Do not use an unauthenticated result as the sole input to a security
> decision. This library does not implement NTS (RFC 8915).

## Authentication

With `NtpClientOptions.AuthenticationKey`, the client adds a MAC to its requests and ignores every reply
that does not end with a valid MAC computed with the same key. The key identifier, the algorithm and the
key must match the configuration of the server, such as a line of the keys file of ntpd or chrony:

<!-- ref:../../samples/Meziantou.Framework.NtpClient.Samples/ReadmeExamples.cs;region=NtpClient_ReadmeAuthentication;format=md-fenced-code-block;dedent=true -->
```csharp
// The line "1 AES128CMAC 000102030405060708090a0b0c0d0e0f" of the keys file of the server
var client = new NtpClient("ntp.example.com", new NtpClientOptions
{
    AuthenticationKey = new NtpAuthenticationKey(
        keyId: 1,
        NtpAuthenticationAlgorithm.AesCmac,
        Convert.FromHexString("000102030405060708090a0b0c0d0e0f")),
});
var response = await client.QueryAsync();
```
<!-- endref -->

## Representable range

NTP timestamps carry 32 bits of seconds, which wraps every 136 years. Following RFC 5905, the client
reads a seconds value with the high bit set as era 0 and one with the high bit clear as era 1, giving a
representable range of 1968-01-20 to 2104-02-26. Timestamps a server leaves unset (all zero) are
returned as `null` rather than as a date in the year 1.

## Migrating from 2.x

- `NtpClient` no longer implements `IDisposable` — it held no resources. Drop the `using`.
- `NtpResponse.ReferenceTimestamp` is now `DateTimeOffset?`, and is `null` when the server did not
  supply one.
- A timed-out query now throws `TimeoutException` instead of `AggregateException`.
- Responses that fail validation are no longer returned. Set `NtpClientOptions.ValidateResponse` to
  `false` for the previous behavior.
- `NtpClientOptions.Timeout` is documented as, and now behaves as, a per-address timeout. It was
  previously shared across every resolved address.
