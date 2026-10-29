---
title: Proxies and HTTP/3
description: Why requests through a proxy and over HTTP/3 are rejected, and how to configure a client whose requests are all validated.
---

The validation runs when `SocketsHttpHandler` opens a TCP connection. Two kinds of requests never give it the real
destination, so the library rejects them rather than appear to protect them.

## Proxies

A request sent through a proxy connects to the proxy, and the proxy then connects to the destination. The library only
sees the connection to the proxy, so the destination cannot be validated, and the request is rejected:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryClientHandlerTests.cs;region=ServerSideRequestForgery_RejectProxy;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();
var innerHandler = new SocketsHttpHandler
{
    UseProxy = true,
    Proxy = new WebProxy("http://127.0.0.1:8080", BypassOnLocal: false),
};
var handler = new ServerSideRequestForgeryClientHandler(innerHandler, options);
using var httpClient = new HttpClient(handler, disposeHandler: true);

// The proxy would choose the destination: the connection is refused instead of being validated against the proxy address
var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("https://example.invalid/")));
Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
```
<!-- endref -->

`SocketsHttpHandler.UseProxy` is `true` by default, and `HttpClient.DefaultProxy` reads the `HTTP_PROXY`, `HTTPS_PROXY`
and `ALL_PROXY` environment variables, so a proxy can be in effect without the application configuring one. Set
`UseProxy = false` on the handler that sends the requests to protect, as in [Getting started](02-GettingStarted.md).

A request the proxy is configured to bypass connects directly to its destination, and is validated normally. The proxy
is read when each connection is opened, so a proxy assigned after the handler was configured is taken into account.

## HTTP/3

An HTTP/3 connection uses QUIC, which does not go through `SocketsHttpHandler.ConnectCallback`, so an HTTP/3 request is
not validated at all. Two requests can use HTTP/3:

- a request whose `Version` is 3.0 or higher, with a `VersionPolicy` other than `RequestVersionOrLower`;
- a request whose `VersionPolicy` is `RequestVersionOrHigher`, whatever its version: the server only has to answer with
  an `Alt-Svc` header naming an HTTP/3 endpoint, on any host and port, for the next request to go there over QUIC.

`ServerSideRequestForgeryClientHandler` rejects every request whose `Version` is 3.0 or higher, or whose
`VersionPolicy` is `RequestVersionOrHigher`, with a `ServerSideRequestForgeryException`:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryClientHandlerTests.cs;region=ServerSideRequestForgery_RejectHttp3;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();
var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);
using var httpClient = new HttpClient(handler, disposeHandler: true);

using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.com/"))
{
    Version = HttpVersion.Version30,
};

// Rejected before the request reaches SocketsHttpHandler, so the exception is not wrapped
await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => httpClient.SendAsync(request));
```
<!-- endref -->

The defaults of `HttpClient` (version 1.1, `RequestVersionOrLower`) pass, and HTTP/2 is still negotiated over TLS with
a `Version` of 2.0. The check is on the version only, not on the scheme, because a plain HTTP request can be redirected
to HTTPS below the handler.

When you call `ConfigureSsrf` without `ServerSideRequestForgeryClientHandler`, nothing rejects these requests. Rule
HTTP/3 out another way: the `DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP3SUPPORT=0` environment variable, the
matching `AppContext` switch, or `HttpClient.DefaultRequestVersion` and `HttpClient.DefaultVersionPolicy` on every
client.
