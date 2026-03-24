# Meziantou.Framework.DnsClient

A comprehensive DNS client library supporting multiple transport protocols, all standard DNS record types, DNSSEC, EDNS(0), internationalized domain names, and reverse DNS lookups.

## Features

- **Multiple protocols**: UDP, TCP, DNS over TLS (DoT), DNS over HTTPS (DoH), DNS over QUIC (DoQ)
- **All DNS record types**: A, AAAA, MX, TXT, CNAME, NS, SOA, SRV, PTR, CAA, NAPTR, SVCB, HTTPS, and more
- **DNSSEC support**: Request and parse DNSKEY, DS, RRSIG, NSEC, NSEC3 records
- **EDNS(0)**: Configurable UDP payload size, DNSSEC OK flag, extended RCODE
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
    new DnsClientOptions { DnssecOk = true });

var query = new DnsQueryMessage { RecursionDesired = true };
query.Questions.Add(new DnsQuestion("cloudflare.com", DnsQueryType.A));
query.EdnsOptions = new DnsEdnsOptions { UdpPayloadSize = 4096, DnssecOk = true };

var response = await client.SendAsync(query);
Console.WriteLine($"Authenticated: {response.Header.AuthenticatedData}");
```
