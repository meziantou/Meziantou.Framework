---
title: Limitations
description: What the DNS packages do not do, so you can plan around it.
---

## DnsClient

- **One server per client.** The client does not read the DNS settings of the machine, does not try several servers,
  and does not resolve names recursively from the root: it asks the server you name.
- **No cache.** Every call sends a query. Cache the answers yourself, for their TTL.
- **No retry after a timeout.** Only a truncated UDP answer is sent again, over TCP. See
  [Errors and timeouts](03-DnsClient/05-ErrorsAndTimeouts.md).
- **No zone transfers or dynamic updates.** `DnsQueryType.AXFR` and `IXFR`, and the `Notify` and `Update` opcodes, can
  be sent with `SendAsync`, but the client reads one response message: it does not support multi-message transfers.
- **No TSIG or SIG(0)** authentication of messages, and no DNS cookies.
- **DNSSEC validation** supports the algorithms 8, 10, 13 and 14 only. See [DNSSEC](03-DnsClient/04-Dnssec.md).
- **No custom certificate validation** for DNS over TLS and DNS over QUIC: the certificate must be trusted by the
  machine. For DNS over HTTPS, set `HttpHandler`.

## DnsServer

- **No zone files, cache, recursion or DNSSEC signing.** The handler builds every answer.
- **ASP.NET Core only.** The server needs a `WebApplication` to map its handler, and TCP and DNS over TLS listeners are
  Kestrel endpoints. The package depends on the ASP.NET Core shared framework and is not marked as compatible with
  trimming.
- **No zone transfers or dynamic updates**: the handler gets the message, but the server sends one response message.
- **No rate limiting.** See [Limits and hardening](04-DnsServer/05-LimitsAndHardening.md).
- **One handler** for every query and every transport.

## DnsFilter

- **DNS rules only.** The web modifiers of ad blocker lists (`$third-party`, `$script`, ...) and cosmetic rules do not
  apply to DNS: rules with unsupported modifiers are skipped. A cosmetic rule with a domain before `##`
  (`example.com##.banner`) is read as a block of that domain: remove such lines from the lists you load.
- **Hosts files block.** The IP address of a hosts entry is ignored. See
  [Formats](05-DnsFilter/02-FilterLists.md#formats).
- **No list download.** The library parses text you provide: download and refresh the lists yourself, or use
  `Meziantou.DnsProxy`.

## DnsProxy

- **Configuration is read at startup**; filter lists are refreshed on their interval.
- **Client names and tags are not known**, so `$client` rules with a name and `$ctag` rules never match. See
  [Filter lists](06-DnsProxy/03-Configuration.md#filter-lists).
- **Rewrites answer the type of the rewrite only**, and blocked names get NXDOMAIN.
- **Local DNSSEC validation does not filter answers.** See [DNSSEC](06-DnsProxy/03-Configuration.md#dnssec).
- **The DNS over HTTPS listener and the diagnostics page are bound to the loopback address.**
- **No persistence**: the query history and the cache are in memory.
- **The certificate is a PFX file.**
