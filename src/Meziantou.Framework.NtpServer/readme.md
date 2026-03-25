# Meziantou.Framework.NtpServer

An NTP server that responds to NTP time queries. Supports NTPv3 and NTPv4.

## Features

- **NTPv3 and NTPv4**: Mirrors the version sent by the client
- **Configurable time source**: Use `TimeProvider` for testability
- **Auto-assigned port**: Use port 0 for testing to get an auto-assigned port
- **OpenTelemetry**: Built-in `ActivitySource` tracing for NTP requests

## Usage

```c#
using Meziantou.Framework.Ntp;

// Start an NTP server on a random port
using var server = new NtpServer(new NtpServerOptions { Port = 0 });
await server.StartAsync();

Console.WriteLine($"NTP server listening on port {server.Port}");
```

### Custom time source

```c#
using var server = new NtpServer(new NtpServerOptions
{
    Port = 0,
    TimeProvider = myCustomTimeProvider,
    Stratum = 2,
});
await server.StartAsync();
```
