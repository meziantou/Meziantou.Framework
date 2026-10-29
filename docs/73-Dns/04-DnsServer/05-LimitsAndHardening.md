---
title: Limits and hardening
description: Set the UDP response size, the idle timeouts and the concurrency limits, and know which malformed or hostile messages the server rejects.
---

## Options

All the limits are properties of `DnsServerOptions`:

<!-- ref:../../../samples/Meziantou.Framework.DnsServer.Samples/ReadmeExamples.cs;region=DnsServer_Limits;format=md-fenced-code-block;dedent=true -->
```csharp
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
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| `MaxUdpResponseSize` | 1232 bytes | The largest UDP response. Between 512 and 65535. |
| `TcpIdleTimeout` | 30 seconds | Closes a TCP or DNS over TLS connection that sends no complete query for that long. `Timeout.InfiniteTimeSpan` disables it. |
| `QuicIdleTimeout` | 30 seconds | Closes an idle DNS over QUIC connection. |
| `MaxConcurrentQueriesPerConnection` | 16 | How many queries a TCP, TLS or QUIC connection can have in progress at once. Further queries wait. |

To cap the number of connections, set the limit of Kestrel, which also covers the HTTP endpoints of the application:

<!-- ref:../../../samples/Meziantou.Framework.DnsServer.Samples/ReadmeExamples.cs;region=DnsServer_KestrelLimits;format=md-fenced-code-block;dedent=true -->
```csharp
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxConcurrentConnections = 1000);
```
<!-- endref -->

## Response size

A UDP response is limited to the payload size the client advertises in its EDNS record, clamped between 512 bytes and
`MaxUdpResponseSize`; without EDNS, 512 bytes. A larger response is truncated, with the TC flag set, so the client asks
again over TCP. Keeping `MaxUdpResponseSize` small also limits how much traffic a spoofed query can make the server
send to its victim.

TCP, TLS, QUIC and HTTPS responses can be up to 65535 bytes, the limit of a DNS message; a larger response is
truncated too.

## What the server rejects

Before the handler runs, the server:

- drops messages that have the QR flag set, which are responses rather than queries (RFC 5625), so two servers cannot
  be made to answer each other indefinitely;
- answers a message it cannot parse with `FormError` (HTTP 400 for DNS over HTTPS), so the client does not wait for a
  timeout;
- answers an EDNS version other than 0 with `BadVersion` (RFC 6891);
- limits decompressed names to 255 bytes and requires compression pointers to point backwards, so a crafted message
  cannot expand without limit;
- for DNS over HTTPS, rejects bodies larger than a DNS message before reading them.

The server does not limit the number of queries per client. A public server needs rate limiting: in the handler, by
`context.RemoteEndPoint`, as `Meziantou.DnsProxy` does, or in front of the server.
