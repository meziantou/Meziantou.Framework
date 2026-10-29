---
title: Overview
description: What the DNS proxy does with each query, and where to find each task.
---

`Meziantou.DnsProxy` is a DNS server for a machine or a network. Clients send it their queries; it blocks the domains
of your filter lists, answers your own records, and forwards the other queries to upstream resolvers over encrypted
transports, with a cache. A web page shows the recent queries. It is a .NET tool built on
[DnsServer](../04-DnsServer/01-Overview.md), [DnsFilter](../05-DnsFilter/01-Overview.md) and
[DnsClient](../03-DnsClient/01-Overview.md).

## What happens to a query

The proxy handles each query in this order, and stops at the first step that answers:

| Step | Answer | Result on the diagnostics page |
| --- | --- | --- |
| 1. Access policy: the client is not in `AllowedClientNetworks` | `Refused` | `NotAllowed` |
| 2. Rate limit: the client sent more than `MaxDnsQueriesPerClientPerMinute` queries this minute | `Refused` | `RateLimited` |
| 3. Custom records: the name and type match a record of `CustomRecords` | The record | `CustomRecord` |
| 4. Filter lists: a rule blocks the name | NXDOMAIN | `Blocked` |
| 4. Filter lists: a `$dnsrewrite` rule matches | The rewritten answer | `Rewritten` |
| 5. Cache: the same question was answered recently | The cached answer | `CacheHit` |
| 6. Upstreams: the first upstream that gives an answer | The upstream answer | `Forwarded`, or `UpstreamFailure` |

Before these steps, a query with an opcode other than `Query` gets `NotImplemented`, and a query without exactly one
question gets `FormError`.

## Pages

- [Running the proxy](02-RunningTheProxy.md): install the tool, start it, and point clients at it.
- [Configuration](03-Configuration.md): upstreams, filter lists, custom records, cache and limits.
- [Serving the network](04-ServingTheNetwork.md): listen on the network, restrict the clients, and enable DNS over
  TLS, HTTPS and QUIC.
- [Diagnostics](05-Diagnostics.md): the web page, pausing the filtering, logs and traces.
