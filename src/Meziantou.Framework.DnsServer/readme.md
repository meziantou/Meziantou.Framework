# Meziantou.Framework.DnsServer

A DNS server library supporting multiple transport protocols with ASP.NET Core hosting integration.

## Features

- **Multiple protocols**: UDP, TCP, DNS over TLS (DoT), DNS over HTTPS (DoH), DNS over QUIC (DoQ)
- **All DNS record types**: A, AAAA, MX, TXT, CNAME, NS, SOA, SRV, PTR, CAA, NAPTR, SVCB, HTTPS, and more
- **DNSSEC record support**: DNSKEY, DS, RRSIG, NSEC, NSEC3 records
- **EDNS(0)**: Configurable UDP payload size, DNSSEC OK flag, extended RCODE
- **ASP.NET Core integration**: Works with WebApplicationBuilder, Kestrel, and endpoint routing
- **Delegate-based handler**: Minimal API-style request handling

## Usage

```c#
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;

var builder = WebApplication.CreateBuilder(args);
builder.AddDnsServer(options =>
{
    options.AddUdpListener(port: 5053);
    options.AddTcpListener(port: 5053);
});

var app = builder.Build();
app.MapDnsHandler(async (context, cancellationToken) =>
{
    var response = context.CreateResponse();
    response.ResponseCode = DnsResponseCode.NoError;

    foreach (var question in context.Query.Questions)
    {
        if (question.Type == DnsQueryType.A)
        {
            response.Answers.Add(new DnsResourceRecord
            {
                Name = question.Name,
                Type = DnsQueryType.A,
                Class = DnsQueryClass.IN,
                TimeToLive = 300,
                Data = new DnsARecordData { Address = System.Net.IPAddress.Parse("127.0.0.1") },
            });
        }
    }

    return response;
});

app.Run();
```

### DNS over HTTPS

```c#
builder.AddDnsServer(options =>
{
    options.AddTcpListener(port: 5053);
});

var app = builder.Build();
app.MapDnsHandler(async (context, ct) => context.CreateResponse());
app.MapDnsOverHttps("/dns-query");

app.Run();
```

### DNS over TLS

```c#
var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
builder.AddDnsServer(options =>
{
    options.AddTlsListener(port: 8853, certificate);
});
```

### DNS over QUIC

```c#
var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
builder.AddDnsServer(options =>
{
    options.AddQuicListener(port: 8853, certificate);
});
```

## Limits and hardening

The server enforces the following limits. All of them are set on `DnsServerOptions`.

```c#
builder.AddDnsServer(options =>
{
    options.AddUdpListener(port: 5053);

    // Largest UDP response, in bytes. A client's advertised EDNS payload size is clamped to this
    // value and larger answers are truncated so the client retries over TCP. The 1232-byte default
    // comes from DNS Flag Day 2020 and also bounds how much a spoofed query can amplify.
    options.MaxUdpResponseSize = 1232;

    // Closes TCP and DNS over TLS connections that go this long without a complete query
    // (RFC 7766 6.2.3). Use Timeout.InfiniteTimeSpan to disable.
    options.TcpIdleTimeout = TimeSpan.FromSeconds(30);

    // Idle timeout for DNS over QUIC connections.
    options.QuicIdleTimeout = TimeSpan.FromSeconds(30);

    // How many queries one connection may have in flight at once.
    options.MaxConcurrentQueriesPerConnection = 16;
});
```

To cap the number of simultaneous connections, set Kestrel's own limit, which also covers any HTTP
endpoints the application exposes:

```c#
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxConcurrentConnections = 1000);
```

Beyond those settings the server:

- rejects messages that arrive with the QR bit set, which RFC 5625 requires so that two servers
  cannot be made to answer each other indefinitely;
- answers a message it cannot parse with `FORMERR` over UDP, TCP, DoT and DoQ, and with HTTP 400
  over DoH, instead of leaving the client to time out;
- answers an EDNS version it does not support with `BADVERS` (RFC 6891 6.1.3);
- bounds domain name decompression to the 255-byte limit of RFC 1035 and requires compression
  pointers to point backwards, so a crafted message cannot expand without limit.

## Notes on writing a handler

- **Response codes.** Set `DnsMessage.ResponseCode` and nothing else, including for the extended
  codes above 15 such as `BadVersion` and `BadCookie`. The encoder splits the value across the
  header and the OPT record, adding an OPT record if the response has none.
- **Domain names.** Names are ASCII on the wire. Convert internationalized names to punycode before
  putting them in a response; a name with non-ASCII characters throws `DnsProtocolException` rather
  than being silently corrupted.
- **Transport.** `context.Protocol` reports `Tls` for DNS over TLS and `Tcp` for plaintext TCP, so a
  handler can require an encrypted transport for particular queries.
- **Truncation.** Responses too large for the transport are automatically truncated with the TC bit
  set; a handler does not need to measure them.
- **DNS over TLS** listeners advertise the `dot` ALPN protocol (RFC 7858 3.1).
