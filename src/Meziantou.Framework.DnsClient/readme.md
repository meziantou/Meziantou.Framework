# Meziantou.Framework.DnsClient

A DNS client library supporting multiple transport protocols, the common DNS record types (with raw access to the rest), DNSSEC validation, EDNS(0), internationalized domain names, and reverse DNS lookups.

## Features

- **Multiple protocols**: UDP, TCP, DNS over TLS (DoT), DNS over HTTPS (DoH), DNS over QUIC (DoQ)
- **Typed record parsing**: A, AAAA, MX, TXT, CNAME, NS, SOA, SRV, PTR, CAA, NAPTR, SVCB, HTTPS, LOC, HINFO, RP, DNAME, URI, TLSA, SSHFP and the DNSSEC types. Any other type is returned as `DnsUnknownRecord` with its raw RDATA.
- **DNSSEC**: request and parse DNSKEY, DS, RRSIG, NSEC and NSEC3 records, plus local chain-of-trust validation against the IANA root anchors
- **EDNS(0)**: configurable UDP payload size (default 1232, per RFC 9715), DNSSEC OK flag, extended RCODE
- **IDN/Punycode**: Automatic Unicode to punycode conversion for internationalized domain names
- **Reverse DNS**: Helper for PTR lookups on IPv4 and IPv6 addresses
- **OpenTelemetry**: Built-in `ActivitySource` tracing for DNS queries

## Usage

```c#
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;

// Simple query using DNS over HTTPS
using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
var response = await client.QueryAsync("example.com", DnsQueryType.A);

foreach (var record in response.Answers.OfType<Response.Records.DnsARecord>())
{
    Console.WriteLine(record.Address);
}
```

### DNS over TLS

```c#
using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Tls);
var response = await client.QueryAsync("example.com", DnsQueryType.AAAA);
```

### Reverse DNS lookup

```c#
using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
var response = await client.ReverseLookupAsync(IPAddress.Parse("1.1.1.1"));

foreach (var ptr in response.Answers.OfType<Response.Records.DnsPtrRecord>())
{
    Console.WriteLine(ptr.DomainName);
}
```

### Internationalized domain names

```c#
using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
// Unicode domain names are automatically converted to punycode
var response = await client.QueryAsync("münchen.de", DnsQueryType.A);
```

### DNSSEC

```c#
using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https,
    new DnsClientOptions { DnssecValidationMode = DnssecValidationMode.Local });

var response = await client.QueryAsync("cloudflare.com", DnsQueryType.A);
Console.WriteLine($"Local DNSSEC validation: {response.DnssecValidationResult.Status}");
```

`DnsResponseHeader.AuthenticatedData` exposes the upstream resolver's AD flag. Use `DnssecValidationMode.Local` when the client must validate the DNSSEC chain locally.

A `Secure` status means the answer records are signed by a chain of trust rooted in the configured anchors **and** that they answer the question that was asked. `Insecure` means the zone is provably unsigned, `Bogus` means validation failed, and `Indeterminate` means validation could not be completed (for example a chain query failed).

## Response validation

Every response is checked against the query it answers: the transaction identifier, the QR bit, the opcode and the echoed question must all match, or `DnsProtocolException` is thrown. Over UDP the socket is also connected to the server so the operating system drops datagrams from other sources, and a truncated (TC) answer is automatically re-issued over TCP.

## Exceptions

`QueryAsync`, `ReverseLookupAsync` and `SendAsync` can throw:

| Exception | Cause |
| --- | --- |
| `DnsProtocolException` | The response is malformed, or does not answer the query that was sent. |
| `TimeoutException` | `DnsClientOptions.Timeout` elapsed. Caller cancellation surfaces as `OperationCanceledException` instead. |
| `OperationCanceledException` | The caller's `CancellationToken` was cancelled. |
| `SocketException`, `IOException` | The transport failed (UDP, TCP, DoT, DoQ). |
| `HttpRequestException` | The DNS over HTTPS request failed. |
| `AuthenticationException` | The TLS handshake failed (DoT). |
| `ObjectDisposedException` | The client was disposed. |
