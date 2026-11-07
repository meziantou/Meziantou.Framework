---
title: Overview
description: How the DNS server is hosted, how a query reaches your handler, and where to find each task.
---

`Meziantou.Framework.DnsServer` handles the DNS protocol: it receives the queries, decodes and checks them, calls your
handler, and encodes, truncates and sends the response. It has no zone files, cache or recursion: the handler decides
what every query returns, from a dictionary, a database, another DNS server, or anything else.

## How it is hosted

The server runs in an ASP.NET Core application:

- `builder.AddDnsServer(options => ...)` adds the listeners. TCP and DNS over TLS listeners are Kestrel endpoints, so
  they need a web host (`WebApplication.CreateBuilder`): without one, [AddDnsServer](cref:Meziantou.Framework.DnsServer.Hosting.DnsServerBuilderExtensions.AddDnsServer) throws
  `InvalidOperationException`. UDP and QUIC listeners are hosted services.
- `app.MapDnsHandler(handler)` registers the handler. Call it exactly once, before the application starts: a second
  call throws `InvalidOperationException`. Until a handler is registered, every query is answered with [ServerFailure](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.ServerFailure).
- `app.MapDnsOverHttps("/dns-query")` adds the DNS over HTTPS endpoints, on the HTTP endpoints of the application.

The listeners start and stop with the application, and log through its logging configuration. See
[Listeners](02-Listeners.md).

## How a query is handled

1. A listener receives the message.
2. The server decodes it. A message it cannot parse is answered with [FormError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.FormError) (HTTP 400 for DNS over HTTPS), a
   message that is itself a response is dropped, and an EDNS version other than 0 is answered with [BadVersion](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.BadVersion).
3. The handler gets a [DnsRequestContext](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext) with the query, the transport and the address of the client, and returns the
   response. See [Writing a handler](03-WritingAHandler.md).
4. The server encodes the response and sends it, truncated if it is too large for the transport.

## Pages

- [Listeners](02-Listeners.md): UDP, TCP, DNS over TLS, DNS over HTTPS and DNS over QUIC, ports and addresses.
- [Writing a handler](03-WritingAHandler.md): read the query, build the response, response codes, errors.
- [Serving a zone](04-ServingAZone.md): answer authoritatively for a domain, with NXDOMAIN, NODATA and SOA records.
- [Limits and hardening](05-LimitsAndHardening.md): response sizes, timeouts, concurrency, and the protections built
  in.
