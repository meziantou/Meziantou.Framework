---
title: Overview
description: Protect an HttpClient that fetches user-supplied URLs by validating the scheme and the IP address of every connection.
---

An application that fetches a URL chosen by a user (a webhook, a link preview, an image import) can be turned against
its own network: the user gives `http://169.254.169.254/` or the name of an internal host, and the server fetches it
on their behalf. This is server-side request forgery (SSRF).

`Meziantou.Framework.Http.ServerSideRequestForgery` protects a `SocketsHttpHandler` against it. Every time the handler
opens a connection, the library:

1. checks the scheme of the request against an allow list (`https` and `wss` by default);
2. resolves the host name, on every connection, so a DNS answer that changes between a check and the connection cannot
   bypass it;
3. checks each resolved IP address against a deny list of networks (loopback, private, link-local, multicast,
   reserved...), and an allow list that takes precedence over it;
4. connects to one of the addresses that passed, chosen by a resolution strategy.

It also rejects what it cannot validate: connections through a proxy, and requests that may be sent over HTTP/3.

## Pages of this group

- [Getting started](02-GettingStarted.md): protect an `HttpClient` and catch a rejected request.
- [Configuration](03-Configuration.md): allowed schemes, blocked and allowed networks, mixed DNS answers, the choice of
  the address.
- [Handling rejections](04-HandlingRejections.md): the exceptions, the log messages and the metrics of a rejection.
- [Proxies and HTTP/3](05-ProxiesAndHttp3.md): why they are rejected, and how to configure a client so its requests are
  validated.

## Namespace

Every type is in the `Meziantou.Framework.Http.ServerSideRequestForgery` namespace.
