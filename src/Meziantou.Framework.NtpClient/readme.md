# Meziantou.Framework.NtpClient

An NTP client library for querying NTP servers to retrieve accurate network time. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Configurable protocol version
- **Clock offset calculation**: Computes the time difference between client and server
- **Round-trip delay**: Measures network round-trip time
- **Response validation**: Rejects replies that do not answer the request that was actually sent
- **Async/await**: Fully asynchronous API with cancellation support
- **OpenTelemetry**: Built-in `ActivitySource` tracing for NTP queries

## Usage

```c#
using Meziantou.Framework.Ntp;

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

### Using NTPv3

```c#
var client = new NtpClient("pool.ntp.org", new NtpClientOptions
{
    Version = NtpVersion.V3,
});
var response = await client.QueryAsync();
```

### Cancellation and timeout

`NtpClientOptions.Timeout` applies to each resolved address separately, so a host name that resolves
to several addresses can take up to `Timeout × addressCount` overall. Use the cancellation token to
bound the total duration.

```c#
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var client = new NtpClient("time.google.com");
var response = await client.QueryAsync(cts.Token);
```

`QueryAsync` throws `TimeoutException` when a query times out, `OperationCanceledException` when the
caller's token is cancelled, and `AggregateException` when several resolved addresses failed for
differing reasons.

### Custom port

```c#
var client = new NtpClient("localhost", new NtpClientOptions
{
    Port = 12345,
});
var response = await client.QueryAsync();
```

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

```c#
var client = new NtpClient("pool.ntp.org", new NtpClientOptions { ValidateResponse = false });
var response = await client.QueryAsync();
if (response.IsKissOfDeath)
{
    Console.WriteLine($"The server refused to answer: {response.KissCode}"); // RATE, DENY, RSTR, ...
}
```

> [!WARNING]
> Validation ties a reply to the request that was sent; it does not authenticate the server. This
> library implements neither NTS (RFC 8915) nor symmetric key authentication, so an attacker who can
> inject packets on the path to the server can still control the reported time. Do not use the result
> as the sole input to a security decision.

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
