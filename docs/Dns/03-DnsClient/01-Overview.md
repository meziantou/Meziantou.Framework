---
title: Overview
description: What DnsClient does, and where to find each task.
---

[DnsClient](cref:Meziantou.Framework.DnsClient.DnsClient) sends DNS queries to one server, over the transport you choose, and returns the parsed response. It is a
client for a resolver or an authoritative server you name: it does not read the DNS configuration of the machine and
does not resolve names recursively by itself.

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp);
var response = await client.QueryAsync("www.example.com", DnsQueryType.A);

Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
var addresses = response.Answers.OfType<DnsARecord>().Select(record => record.Address).ToList();
Assert.Equal([IPAddress.Parse("192.0.2.10")], addresses);
```
<!-- endref -->

## Pages

- [Protocols](02-Protocols.md): UDP, TCP, DNS over TLS, DNS over HTTPS and DNS over QUIC, the format of the server
  address, and the HTTP options.
- [Querying](03-Querying.md): record types, typed records, response codes, reverse lookups, internationalized names,
  and custom query messages.
- [DNSSEC](04-Dnssec.md): request the signatures, read the resolver's AD flag, or validate the chain of trust locally.
- [Errors and timeouts](05-ErrorsAndTimeouts.md): the exceptions, the timeout, how responses are checked, and tracing.

## Options

[DnsClientOptions](cref:Meziantou.Framework.DnsClient.DnsClientOptions) is the third argument of the constructor:

| Option | Default | Use |
| --- | --- | --- |
| [Timeout](cref:Meziantou.Framework.DnsClient.DnsClientOptions.Timeout) | 5 seconds | How long to wait for a response. `Timeout.InfiniteTimeSpan` disables it. See [Errors and timeouts](05-ErrorsAndTimeouts.md). |
| [EnableEdns](cref:Meziantou.Framework.DnsClient.DnsClientOptions.EnableEdns) | `true` | Adds an EDNS(0) OPT record to the queries. |
| [EdnsUdpPayloadSize](cref:Meziantou.Framework.DnsClient.DnsClientOptions.EdnsUdpPayloadSize) | 1232 | The largest UDP response the client accepts, advertised in the OPT record. |
| [RetryTruncatedOverTcp](cref:Meziantou.Framework.DnsClient.DnsClientOptions.RetryTruncatedOverTcp) | `true` | Sends the query again over TCP when a UDP response is truncated. See [Truncated responses](02-Protocols.md#truncated-responses). |
| [DnssecOk](cref:Meziantou.Framework.DnsClient.DnsClientOptions.DnssecOk) | `false` | Asks for the DNSSEC records. See [DNSSEC](04-Dnssec.md). |
| [DnssecValidationMode](cref:Meziantou.Framework.DnsClient.DnssecValidationMode) | [None](cref:Meziantou.Framework.DnsClient.DnssecValidationMode.None) | [Local](cref:Meziantou.Framework.DnsClient.DnssecValidationMode.Local) validates the DNSSEC chain of trust. See [DNSSEC](04-Dnssec.md#validate-locally). |
| [DnssecTrustAnchors](cref:Meziantou.Framework.DnsClient.DnssecTrustAnchors) | [DnssecTrustAnchors.Root](cref:Meziantou.Framework.DnsClient.DnssecTrustAnchors.Root) | The trust anchors of local validation. |
| [TimeProvider](cref:Meziantou.Framework.DnsClient.DnsClientOptions.TimeProvider) | `TimeProvider.System` | The clock used to check the validity period of DNSSEC signatures. |
| [HttpHandler](cref:Meziantou.Framework.DnsClient.DnsClientOptions.HttpHandler), [HttpVersion](cref:Meziantou.Framework.DnsClient.DnsClientOptions.HttpVersion), [HttpVersionPolicy](cref:Meziantou.Framework.DnsClient.DnsClientOptions.HttpVersionPolicy) | a pooled handler, HTTP/2, `RequestVersionOrLower` | DNS over HTTPS only. See [DNS over HTTPS](02-Protocols.md#dns-over-https). |
| [ServerAddressResolver](cref:Meziantou.Framework.DnsClient.DnsClientOptions.ServerAddressResolver) | the system resolver | Resolves the host name of the server. See [Server address](02-Protocols.md#server-address). |
