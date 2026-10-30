---
title: Introduction
description: Four packages to query DNS servers, answer DNS queries, filter domains, and run a filtering DNS proxy.
---

The DNS packages cover both sides of the protocol, plus filtering:

| Package | Use it to |
| --- | --- |
| `Meziantou.Framework.DnsClient` | Send queries to a DNS server and read typed records, with optional DNSSEC validation. See [DnsClient](03-DnsClient/01-Overview.md). |
| `Meziantou.Framework.DnsServer` | Answer DNS queries from your own code, in an ASP.NET Core application. See [DnsServer](04-DnsServer/01-Overview.md). |
| `Meziantou.Framework.DnsFilter` | Parse block lists (hosts files, domain lists, Adblock-style rules) and decide whether a domain is blocked, allowed or rewritten. See [DnsFilter](05-DnsFilter/01-Overview.md). |
| `Meziantou.DnsProxy` | Run a DNS proxy that filters queries and forwards the others to upstream resolvers. It is a .NET tool built on the three libraries. See [DnsProxy](06-DnsProxy/01-Overview.md). |

## Protocols

The client and the server support the same transports:

| Transport | [DnsClientProtocol](cref:Meziantou.Framework.DnsClient.DnsClientProtocol) / [DnsServerProtocol](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol) | Default port | Standard |
| --- | --- | --- | --- |
| UDP | `Udp` | 53 | RFC 1035 |
| TCP | `Tcp` | 53 | RFC 7766 |
| DNS over TLS (DoT) | `Tls` | 853 | RFC 7858 |
| DNS over HTTPS (DoH) | `Https` | 443 (the port of the URL) | RFC 8484 |
| DNS over QUIC (DoQ) | `Quic` | 853 | RFC 9250 |

DNS over QUIC needs QUIC support from the operating system (`System.Net.Quic`). Where it is missing, the client throws
`PlatformNotSupportedException` and the server does not start its QUIC listeners. See
[Protocols](03-DnsClient/02-Protocols.md#dns-over-quic).

Both sides also support EDNS(0) (larger UDP messages, the DNSSEC OK flag, extended response codes) and every common
record type, from A and MX to SVCB, HTTPS and the DNSSEC records.

## Requirements

- `Meziantou.Framework.DnsServer` depends on the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`), which the
  .NET SDK provides, also in console and test projects.
- `Meziantou.Framework.DnsClient` and `Meziantou.Framework.DnsFilter` are compatible with trimming.
- `Meziantou.DnsProxy` needs the ASP.NET Core runtime.

## Next steps

- [Getting started](02-GettingStarted.md): send a query, and answer one.
- [Limitations](07-Limitations.md): what the packages do not do.
