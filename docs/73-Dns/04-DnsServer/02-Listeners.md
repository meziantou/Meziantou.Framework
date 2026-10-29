---
title: Listeners
description: Listen over UDP, TCP, DNS over TLS, DNS over HTTPS and DNS over QUIC, and choose the ports and the addresses.
---

Each `Add…Listener` call of `DnsServerOptions` adds an endpoint. An application can mix them, and use several of each
type:

| Method | Transport | Usual port |
| --- | --- | --- |
| `AddUdpListener(port, bindAddress)` | UDP | 53 (the default of `port`) |
| `AddTcpListener(port, bindAddress)` | TCP | 53 (the default of `port`) |
| `AddTlsListener(port, certificate, bindAddress)` | DNS over TLS | 853 |
| `AddQuicListener(port, certificate, bindAddress)` | DNS over QUIC | 853 |
| `MapDnsOverHttps(pattern)` (on the application) | DNS over HTTPS | 443, an HTTPS endpoint of Kestrel |

Most clients send their queries over UDP and fall back to TCP when an answer is truncated, so a server that answers
over UDP should also listen over TCP on the same port, as in
[Getting started](../02-GettingStarted.md#answer-dns-queries).

## Addresses

`bindAddress` defaults to `IPAddress.Any`: every IPv4 interface of the machine. Pass `IPAddress.Loopback` to only
accept queries from the local machine, and add a listener on `IPAddress.IPv6Any` or `IPAddress.IPv6Loopback` to accept
IPv6 clients.

A server that answers every client of the internet can be used to amplify attacks or to query your data: bind it to the
addresses you need, and restrict the clients in the handler, by `context.RemoteEndPoint`, when it is not meant to be
public.

On Linux and macOS, ports below 1024, such as 53, need elevated privileges. Use a port such as 5053 during development.

## DNS over TLS

`AddTlsListener` takes the certificate the server presents. Clients check it against the name they connect to:

<!-- ref:../../../samples/Meziantou.Framework.DnsServer.Samples/ReadmeExamples.cs;region=DnsServer_DnsOverTls;format=md-fenced-code-block;dedent=true -->
```csharp
var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
builder.AddDnsServer(options =>
{
    options.AddTlsListener(port: 8853, certificate);
});
```
<!-- endref -->

The listener advertises the `dot` ALPN protocol (RFC 7858). `context.Protocol` is `Tls` for these queries, and `Tcp`
for plain TCP, so the handler can require encryption for some names.

## DNS over HTTPS

DNS over HTTPS uses the HTTP endpoints of the application. `MapDnsOverHttps` maps the path, and the endpoint itself is
an HTTPS endpoint of Kestrel:

<!-- ref:../../../tests/Meziantou.Framework.DnsServer.Tests/DnsServerIntegrationTests.cs;region=DnsServer_DnsOverHttpsLocal;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Port 0: the system picks a free port
    kestrel.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.UseHttps(certificate));
});
builder.AddDnsServer(options => { }); // DNS over HTTPS needs no DNS listener

await using var app = builder.Build();
app.MapDnsHandler((context, cancellationToken) =>
{
    var response = context.CreateResponse();
    response.Answers.Add(new DnsResourceRecord
    {
        Name = "www.example.com",
        Type = DnsQueryType.A,
        Class = DnsQueryClass.IN,
        TimeToLive = 300,
        Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.10") },
    });
    return ValueTask.FromResult(response);
});
app.MapDnsOverHttps("/dns-query");
await app.StartAsync();

var url = app.Urls.First() + "/dns-query"; // https://127.0.0.1:<port>/dns-query
```
<!-- endref -->

- `MapDnsOverHttps` accepts `POST` requests with an `application/dns-message` body, and `GET` requests with the query
  in the `dns` parameter, base64url-encoded (RFC 8484).
- A malformed message gets HTTP 400, a body of another content type HTTP 415, and a message larger than a DNS message
  can be HTTP 413.
- The response has a `Cache-Control: max-age` header with the lowest TTL of its records; for an answer without
  records, the SOA minimum of the authority section caps it. A response without any TTL gets `no-store`.
- `MapDnsOverHttps` returns an `IEndpointConventionBuilder`, so the usual endpoint conventions apply, such as
  `RequireHost` or rate limiting.
- `AddDnsServer` is still needed, even without listeners: it registers the services the endpoint uses.

### Kestrel endpoints

TCP and DNS over TLS listeners are endpoints that `AddDnsServer` configures in code. When Kestrel has endpoints
configured in code, it ignores the URLs of the configuration (`ASPNETCORE_URLS`, `--urls`): configure the HTTP
endpoints of the application in code too, with `ConfigureKestrel`, as above. When the application only has UDP or QUIC
listeners, Kestrel keeps its default HTTP endpoint unless you configure one.

## DNS over QUIC

`AddQuicListener` takes the certificate the server presents:

<!-- ref:../../../samples/Meziantou.Framework.DnsServer.Samples/ReadmeExamples.cs;region=DnsServer_DnsOverQuic;format=md-fenced-code-block;dedent=true -->
```csharp
var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
builder.AddDnsServer(options =>
{
    options.AddQuicListener(port: 8853, certificate);
});
```
<!-- endref -->

The listener advertises the `doq` ALPN protocol (RFC 9250), and accepts at most `MaxConcurrentQueriesPerConnection`
streams at once per connection. QUIC needs support from the operating system: where it is missing, the server logs a
warning and does not start its QUIC listeners; the other listeners run normally. `QuicListener.IsSupported` tells
whether QUIC is available.

## Startup errors

A listener that cannot bind its port (port in use, missing privileges) fails the start of the application: `app.Run()`
or `await app.StartAsync()` throws.
