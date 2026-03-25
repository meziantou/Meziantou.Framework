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
