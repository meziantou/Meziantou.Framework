---
title: Measuring accurately
description: Query a server several times and keep the best sample, or query several servers and let the majority outvote a wrong one.
---

A single query is one measurement: a reply delayed in a queue on the way back makes its offset wrong, and a server whose
clock is wrong gives a wrong offset. The client can combine several measurements, as NTP implementations do (RFC 5905).

## Several samples from one server

[MeasureAsync](cref:Meziantou.Framework.Ntp.NtpClient.MeasureAsync) queries the server several times and keeps the sample with the shortest round-trip delay: the one that
spent the least time in queues, whose offset is the most accurate.

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_Measure;format=md-fenced-code-block;dedent=true -->
```csharp
var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = port });

// 4 queries, 2 seconds apart, by default
var measurement = await client.MeasureAsync(new NtpMeasurementOptions
{
    SampleCount = 4,
    SampleInterval = TimeSpan.FromMilliseconds(100),
});

// The offset of the sample with the shortest round-trip delay
var offset = measurement.ClockOffset;

// The maximum error of the offset, and how much the samples disagree
var maximumError = measurement.RootDistance;
var jitter = measurement.Jitter;

Assert.Equal(4, measurement.Samples.Count + measurement.Errors.Count);
```
<!-- endref -->

The [NtpServerMeasurement](cref:Meziantou.Framework.Ntp.NtpServerMeasurement) it returns contains:

| Property | Contains |
| --- | --- |
| [BestSample](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.BestSample), [ClockOffset](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.ClockOffset), [RoundTripDelay](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.RoundTripDelay) | The sample with the shortest round-trip delay, and its offset and delay |
| [Samples](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.Samples), [Errors](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.Errors) | Every valid reply, and the error of every query that got none |
| [Jitter](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.Jitter) | How much the offsets of the samples differ from the best one |
| [RootDistance](cref:Meziantou.Framework.Ntp.NtpServerMeasurement.RootDistance) | The maximum error of the offset: half the delay to the server and to its reference clock, plus the dispersion and the precision the server reports, plus the jitter |

The call fails only when no query gets a valid reply: it throws `TimeoutException` when they all time out, and
`AggregateException` when they fail for different reasons.

| Option | Default | Use |
| --- | --- | --- |
| [SampleCount](cref:Meziantou.Framework.Ntp.NtpMeasurementOptions.SampleCount) | 4 | The number of queries. |
| [SampleInterval](cref:Meziantou.Framework.Ntp.NtpMeasurementOptions.SampleInterval) | 2 seconds | The delay between two queries. Public servers rate-limit their clients: do not set it to zero with them. |

## Several servers

Querying several servers protects against a server whose clock is wrong. The static [NtpClient.MeasureAsync](cref:Meziantou.Framework.Ntp.NtpClient.MeasureAsync) takes
one client per server, measures them concurrently, and applies the selection algorithm of NTP: the offset of each server
is an interval, its offset plus or minus its root distance, and the servers whose intervals do not overlap the ones of
the majority are discarded. The offsets of the others are averaged, weighted by their accuracy.

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpClient_MeasureServers;format=md-fenced-code-block;dedent=true -->
```csharp
// Three servers, one of which is one hour ahead
using var server1 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
using var server2 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
using var wrongServer = new NtpServer(new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,
    TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1)),
});
await server1.StartAsync();
await server2.StartAsync();
await wrongServer.StartAsync();

NtpClient[] clients =
[
    new NtpClient("127.0.0.1", new NtpClientOptions { Port = server1.Port }),
    new NtpClient("127.0.0.1", new NtpClientOptions { Port = server2.Port }),
    new NtpClient("127.0.0.1", new NtpClientOptions { Port = wrongServer.Port }),
];
var measurement = await NtpClient.MeasureAsync(clients, new NtpMeasurementOptions { SampleCount = 2, SampleInterval = TimeSpan.Zero });

// The server that is one hour ahead is outvoted
Assert.Equal(2, measurement.SelectedServers.Count);
Assert.Same(clients[2], Assert.Single(measurement.RejectedServers).Client);

// The offset to add to the local clock, and its bounds
Assert.InRange(measurement.ClockOffset, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
Assert.InRange(measurement.ClockOffset, measurement.MinimumClockOffset, measurement.MaximumClockOffset);
```
<!-- endref -->

- Query at least 3 servers: with 2 servers that disagree, there is no majority. Each client is one vote, so do not query
  the same server twice, for instance through two names.
- Each client has its own options: its port, its timeout, its key.
- [SelectedServers](cref:Meziantou.Framework.Ntp.NtpClockMeasurement.SelectedServers) agree with the majority, [RejectedServers](cref:Meziantou.Framework.Ntp.NtpClockMeasurement.RejectedServers) do not, and [FailedServers](cref:Meziantou.Framework.Ntp.NtpClockMeasurement.FailedServers) gave no valid reply,
  with the exception of each.
- The true offset lies between [MinimumClockOffset](cref:Meziantou.Framework.Ntp.NtpClockMeasurement.MinimumClockOffset) and [MaximumClockOffset](cref:Meziantou.Framework.Ntp.NtpClockMeasurement.MaximumClockOffset), as long as the majority of the servers is right.

The call throws [NtpClockSelectionException](cref:Meziantou.Framework.Ntp.NtpClockSelectionException) when servers answered but no majority of them agrees; its
[Measurements](cref:Meziantou.Framework.Ntp.NtpClockSelectionException.Measurements) property lists what each one answered. When no server answers, it throws `TimeoutException` or
`AggregateException`.

## Retrying a lost request

[RetryCount](cref:Meziantou.Framework.Ntp.NtpClientOptions.RetryCount) sends a new request when no valid reply arrives within [Timeout](cref:Meziantou.Framework.Ntp.NtpClientOptions.Timeout). It applies to [QueryAsync](cref:Meziantou.Framework.Ntp.NtpClient.QueryAsync) and to
each query of [MeasureAsync](cref:Meziantou.Framework.Ntp.NtpClient.MeasureAsync). See [Retries](03-QueryingAServer.md#retries).
