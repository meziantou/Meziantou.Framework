---
title: Zone transfers
description: Copy a whole zone with AXFR, or the changes since the version you have with IXFR.
---

The examples query a server that holds the version 2 of an `example.com` zone, whose version 1 had another address for
`www.example.com`.

## Transfer a whole zone

[TransferZoneAsync](cref:Meziantou.Framework.DnsClient.DnsClient.TransferZoneAsync) sends an AXFR query and returns the messages of the transfer as they arrive. A large zone
spans many messages: read them with `await foreach`, and take the records from their [Answers](cref:Meziantou.Framework.DnsClient.Response.DnsResponseMessage.Answers):

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_ZoneTransfer;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Tcp);

var records = new List<DnsRecord>();
await foreach (var message in client.TransferZoneAsync("example.com"))
{
    Assert.Equal(DnsResponseCode.NoError, message.Header.ResponseCode);
    records.AddRange(message.Answers);
}

// The transfer starts and ends with the SOA record of the zone
Assert.Equal(2u, Assert.IsType<DnsSoaRecord>(records[0]).Serial);
Assert.Equal(2u, Assert.IsType<DnsSoaRecord>(records[^1]).Serial);

var addresses = records.OfType<DnsARecord>().Select(record => $"{record.Name} {record.Address}").ToList();
Assert.Equal(["www.example.com 192.0.2.11", "mail.example.com 192.0.2.20"], addresses);
```
<!-- endref -->

- The records start with the [DnsSoaRecord](cref:Meziantou.Framework.DnsClient.Response.Records.DnsSoaRecord) of the zone and end with the same record.
- A server that refuses the transfer, which servers do unless your address or your [TSIG key](07-TsigAndCookies.md)
  is allowed, answers with one message whose response code is [Refused](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.Refused) or [NotAuthoritative](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.NotAuthoritative): it is
  not an exception.

## Transfer the changes

The overload that takes a serial sends an IXFR query: the server returns the changes since the version you have. The
records start and end with the SOA record of the current version. In between, each version has its old SOA record
followed by the records it deleted, then its new SOA record followed by the records it added:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_IncrementalZoneTransfer;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Tcp);

// The client has the version 1 of the zone
var records = new List<DnsRecord>();
await foreach (var message in client.TransferZoneAsync("example.com", currentSerial: 1))
{
    records.AddRange(message.Answers);
}

// The current SOA record, then each version: its old SOA record and the deleted records, its new SOA record and
// the added records, and the current SOA record again
var lines = records.Select(record => record switch
{
    DnsSoaRecord soa => $"SOA {soa.Serial}",
    DnsARecord a => $"A {a.Name} {a.Address}",
    _ => record.RecordType.ToString(),
}).ToList();
Assert.Equal(["SOA 2", "SOA 1", "A www.example.com 192.0.2.10", "SOA 2", "A www.example.com 192.0.2.11", "SOA 2"], lines);
```
<!-- endref -->

The server may answer otherwise:

- When your version is current, the answer is the SOA record alone.
- When the server does not keep the changes, it sends the whole zone, as an AXFR does: the second record is not an SOA
  record.

## Transport and checks

- The transfer goes over TCP for a [Udp](cref:Meziantou.Framework.DnsClient.DnsClientProtocol.Udp) or [Tcp](cref:Meziantou.Framework.DnsClient.DnsClientProtocol.Tcp) client, and over TLS for a [Tls](cref:Meziantou.Framework.DnsClient.DnsClientProtocol.Tls) client (zone
  transfer over TLS, RFC 9103). [Https](cref:Meziantou.Framework.DnsClient.DnsClientProtocol.Https) and [Quic](cref:Meziantou.Framework.DnsClient.DnsClientProtocol.Quic) clients throw `NotSupportedException`.
- [Timeout](cref:Meziantou.Framework.DnsClient.DnsClientOptions.Timeout) applies to each message, not to the whole transfer: a large zone can take longer than the timeout,
  as long as the messages keep coming.
- Each message must have the ID of the query; the first one must also echo the question. The transfer must start with
  an SOA record and end with the SOA record of the same version, with nothing after it. Otherwise, or when the server
  closes the connection before the end, the enumeration throws [DnsProtocolException](cref:Meziantou.Framework.DnsClient.DnsProtocolException).
- Local [DNSSEC validation](04-Dnssec.md#validate-locally) does not apply to transfers. Sign the transfer with
  [TSIG](07-TsigAndCookies.md) to authenticate it.
