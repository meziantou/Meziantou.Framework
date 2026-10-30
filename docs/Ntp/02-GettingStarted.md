---
title: Getting started
description: Install the packages, start an NTP server on the local machine, and query it.
---

## Install the packages

```bash
dotnet add package Meziantou.Framework.NtpClient
dotnet add package Meziantou.Framework.NtpServer
```

## Query a server

This example starts a server on a free port of the loopback interface, then queries it. To query a time server of your
network or of the internet, pass its host name to [NtpClient](cref:Meziantou.Framework.Ntp.NtpClient) and leave the default port, 123:

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
await server.StartAsync();

var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
var response = await client.QueryAsync();

// The difference between the clock of the server and the local clock
var localTimeCorrected = DateTimeOffset.UtcNow + response.ClockOffset;

Assert.Equal(NtpVersion.V4, response.Version);
Assert.Equal(2, response.Stratum);
Assert.True(response.RoundTripDelay >= TimeSpan.Zero);
```
<!-- endref -->

- `Port = 0` lets the system choose a free port; `server.Port` is the port it chose.
- [QueryAsync](cref:Meziantou.Framework.Ntp.NtpClient.QueryAsync) sends one request and returns an [NtpResponse](cref:Meziantou.Framework.Ntp.NtpResponse). See [Querying a server](03-QueryingAServer.md).
- [ClockOffset](cref:Meziantou.Framework.Ntp.NtpResponse.ClockOffset) is what to add to the local clock to get the time of the server.
- [NtpClient](cref:Meziantou.Framework.Ntp.NtpClient) holds no resource: there is nothing to dispose. The server is disposable: disposing it stops it.

## Next steps

- [Querying a server](03-QueryingAServer.md): the fields of the response, the options, timeouts.
- [Running a server](05-RunningAServer.md): addresses, time source, stratum, rate limiting.
