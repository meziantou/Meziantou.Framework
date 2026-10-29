---
title: Protocols
description: Choose the transport, write the server address, configure DNS over HTTPS, and handle truncated UDP answers.
---

The second argument of the constructor is the transport, and the first one the server, in the format of that
transport. In this example, `dns.example.net` stands for the resolver you use:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_Protocols;format=md-fenced-code-block;dedent=true -->
```csharp
// UDP and TCP: an IP address or a host name, port 53 by default
using var udpClient = new DnsClient("192.0.2.53", DnsClientProtocol.Udp);
using var tcpClient = new DnsClient("192.0.2.53:5353", DnsClientProtocol.Tcp);

// DNS over TLS and DNS over QUIC: port 853 by default; the certificate must match the host name
using var tlsClient = new DnsClient("dns.example.net", DnsClientProtocol.Tls);
using var quicClient = new DnsClient("dns.example.net:853", DnsClientProtocol.Quic);

// DNS over HTTPS: the URL of the endpoint, https only
using var httpsClient = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https);
```
<!-- endref -->

## Choose a transport

| Transport | Use it when |
| --- | --- |
| `Udp` | The server is on a network you trust. It is the fastest: one datagram each way. |
| `Tcp` | Answers are large (zone data, many records), or UDP is blocked. |
| `Tls` | The queries cross a network you do not trust, and port 853 is open. |
| `Https` | The queries cross a network you do not trust, and only HTTPS goes through. |
| `Quic` | The server supports DNS over QUIC and the machine supports QUIC. |

`Udp` and `Tcp` send the queries in clear text. `Tls`, `Https` and `Quic` encrypt them and check the certificate of the
server with the trust store of the machine.

## Server address

For `Udp`, `Tcp`, `Tls` and `Quic`, the server is an IP address or a host name, with an optional port:

| Address | Port |
| --- | --- |
| `192.0.2.53`, `2001:db8::53`, `dns.example.net` | The default port: 53 for UDP and TCP, 853 for TLS and QUIC |
| `192.0.2.53:5353`, `dns.example.net:5353` | 5353 |
| `[2001:db8::53]:5353` | 5353: an IPv6 address with a port goes in brackets |

- A host name is resolved once, when the client is created, and the client keeps the first address. Create a new
  client to pick up a change of address.
- For `Tls` and `Quic`, the host name is also the name checked against the certificate of the server. Pass the host
  name rather than its IP address unless the certificate contains the IP address.
- The constructor throws when the address is invalid (`ArgumentException`) and when the host name cannot be resolved.

By default the host name is resolved by the system resolver. To resolve it another way, for instance with a fixed
address, set `ServerAddressResolver`:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_ServerAddressResolver;format=md-fenced-code-block;dedent=true -->
```csharp
// Resolves the host name of the DNS server itself, instead of asking the system resolver
var client = new DnsClient("dns.example.net", DnsClientProtocol.Tls, new DnsClientOptions
{
    ServerAddressResolver = hostName => [IPAddress.Parse("192.0.2.53")],
});
```
<!-- endref -->

## DNS over HTTPS

For `Https`, the server is the URL of the endpoint, such as `https://dns.example.net/dns-query`. The scheme must be
`https`: an `http://` URL throws `ArgumentException`, because it would send the queries in clear text.

The client sends the queries with `HttpClient`. The HTTP options apply to this transport only:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_HttpsOptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};

using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
{
    HttpHandler = handler,
    HttpVersion = HttpVersion.Version30,
    HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    Timeout = TimeSpan.FromSeconds(10),
});

var response = await client.QueryAsync("example.com", DnsQueryType.A);
```
<!-- endref -->

- `HttpHandler`: the handler of the `HttpClient`, for a proxy, client certificates or connection settings. The client
  does not dispose it. Without one, the client creates a `SocketsHttpHandler` that renews its connections every two
  minutes.
- `HttpVersion`: HTTP/2 by default, which every DNS over HTTPS server supports. Requesting HTTP/3 costs a failed QUIC
  attempt per connection on machines or servers without it.
- `HttpVersionPolicy`: `RequestVersionOrLower` by default, so the client falls back to an older version.

## DNS over QUIC

DNS over QUIC needs QUIC support from the operating system. Where it is missing, the constructor throws
`PlatformNotSupportedException`, before any query, so you can fall back to another transport:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_QuicFallback;format=md-fenced-code-block;dedent=true -->
```csharp
DnsClient client;
try
{
    client = new DnsClient("dns.example.net", DnsClientProtocol.Quic);
}
catch (PlatformNotSupportedException)
{
    // QUIC is not available on this machine
    client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https);
}
```
<!-- endref -->

`QuicConnection.IsSupported` tells the same thing without creating a client.

## Truncated responses

A UDP response is limited in size. When an answer does not fit, the server sets the truncation (TC) flag, and the
client sends the query again over TCP, to the same server, as RFC 1035 requires. Set `RetryTruncatedOverTcp` to
`false` to get the truncated response instead:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_Truncation;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp);
var response = await client.QueryAsync("big.example.com", DnsQueryType.TXT);

// The answer does not fit in a UDP message: the client asked again over TCP
Assert.False(response.Header.IsTruncated);
Assert.HasCount(40, response.Answers);

using var udpOnlyClient = new DnsClient(serverAddress, DnsClientProtocol.Udp, new DnsClientOptions
{
    RetryTruncatedOverTcp = false,
});
var truncatedResponse = await udpOnlyClient.QueryAsync("big.example.com", DnsQueryType.TXT);
Assert.True(truncatedResponse.Header.IsTruncated);
```
<!-- endref -->

The retry only applies to `Udp`. `EdnsUdpPayloadSize` (1232 bytes by default, as RFC 9715 recommends) is the size the
client advertises to the server; a larger value lets more answers fit in UDP, but large datagrams are often dropped by
the network.
