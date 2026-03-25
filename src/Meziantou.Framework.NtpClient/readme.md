# Meziantou.Framework.NtpClient

An NTP client library for querying NTP servers to retrieve accurate network time. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Configurable protocol version
- **Clock offset calculation**: Computes the time difference between client and server
- **Round-trip delay**: Measures network round-trip time
- **Async/await**: Fully asynchronous API with cancellation support
- **OpenTelemetry**: Built-in `ActivitySource` tracing for NTP queries

## Usage

```c#
using Meziantou.Framework.Ntp;

// Query an NTP server using NTPv4 (default)
using var client = new NtpClient("pool.ntp.org");
var response = await client.QueryAsync();

Console.WriteLine($"Server time: {response.TransmitTimestamp}");
Console.WriteLine($"Clock offset: {response.ClockOffset}");
Console.WriteLine($"Round-trip delay: {response.RoundTripDelay}");
Console.WriteLine($"Stratum: {response.Stratum}");
```

### Using NTPv3

```c#
using var client = new NtpClient("pool.ntp.org", new NtpClientOptions
{
    Version = NtpVersion.V3,
});
var response = await client.QueryAsync();
```

### Cancellation and timeout

```c#
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var client = new NtpClient("time.google.com");
var response = await client.QueryAsync(cts.Token);
```

### Custom port

```c#
using var client = new NtpClient("localhost", new NtpClientOptions
{
    Port = 12345,
});
var response = await client.QueryAsync();
```
