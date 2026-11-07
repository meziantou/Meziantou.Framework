---
title: Getting started
description: Install the package, protect an HttpClient with ServerSideRequestForgeryClientHandler and catch rejected requests.
---

## Install the package

<InstallNuGetPackage Id="Meziantou.Framework.Http.ServerSideRequestForgery" />

The examples omit the `using` directives: they need `using Meziantou.Framework.Http.ServerSideRequestForgery;`.

## Protect an HttpClient

Wrap a `SocketsHttpHandler` in a [ServerSideRequestForgeryClientHandler](cref:Meziantou.Framework.Http.ServerSideRequestForgery.ServerSideRequestForgeryClientHandler), and send the requests that target
user-supplied URLs through it:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryClientHandlerTests.cs;region=ServerSideRequestForgeryClientHandler_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();
var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);
using var client = new HttpClient(handler, disposeHandler: true);

// Requests to a loopback or private address are rejected before connecting
var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://127.0.0.1/")));
Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
```
<!-- endref -->

- The default options block the loopback, private, link-local and reserved networks, and allow the `https` and `wss`
  schemes only. See [Configuration](03-Configuration.md).
- `UseProxy = false` matters: a request sent through a proxy cannot be validated, so it is rejected. `SocketsHttpHandler`
  uses the proxy of the `HTTP_PROXY`, `HTTPS_PROXY` and `ALL_PROXY` environment variables by default. See
  [Proxies and HTTP/3](05-ProxiesAndHttp3.md).
- The constructor configures the `SocketsHttpHandler` you pass: create one for this purpose, and do not share it with a
  client that must reach internal hosts.

## Catch a rejected request

A rejected connection surfaces as an `HttpRequestException` whose `InnerException` is a
[ServerSideRequestForgeryException](cref:Meziantou.Framework.Http.ServerSideRequestForgery.ServerSideRequestForgeryException), because the runtime wraps what the connection callback throws:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryConnectPipelineTests.cs;region=ServerSideRequestForgery_CatchRejection;format=md-fenced-code-block;dedent=true -->
```csharp
ServerSideRequestForgeryException? rejection = null;
try
{
    // 127.0.0.1 is in the default UnsafeIpNetworks
    using var response = await httpClient.GetAsync(new Uri("https://127.0.0.1/"), cancellationToken);
}
catch (HttpRequestException ex) when (ex.InnerException is ServerSideRequestForgeryException ssrf)
{
    rejection = ssrf;
}

Assert.NotNull(rejection);
```
<!-- endref -->

An HTTP/3 request is rejected before it reaches the `SocketsHttpHandler`, so that one arrives as a
[ServerSideRequestForgeryException](cref:Meziantou.Framework.Http.ServerSideRequestForgery.ServerSideRequestForgeryException) directly. See [Handling rejections](04-HandlingRejections.md).

## Configure a SocketsHttpHandler directly

[ConfigureSsrf](cref:Meziantou.Framework.Http.ServerSideRequestForgery.SocketHttpHandlerServerSideRequestForgeryExtensions.ConfigureSsrf) applies the same validation to a `SocketsHttpHandler` without the wrapping handler, and returns the
handler:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryConnectPipelineTests.cs;region=ServerSideRequestForgery_ConfigureSsrf;format=md-fenced-code-block;dedent=true -->
```csharp
var handler = new SocketsHttpHandler { UseProxy = false };
handler.ConfigureSsrf(options);
```
<!-- endref -->

Prefer [ServerSideRequestForgeryClientHandler](cref:Meziantou.Framework.Http.ServerSideRequestForgery.ServerSideRequestForgeryClientHandler): [ConfigureSsrf](cref:Meziantou.Framework.Http.ServerSideRequestForgery.SocketHttpHandlerServerSideRequestForgeryExtensions.ConfigureSsrf) alone does not reject the requests that may use HTTP/3,
which are not validated at all. See [Proxies and HTTP/3](05-ProxiesAndHttp3.md).

Both work by setting `SocketsHttpHandler.ConnectCallback`. A handler that already has a `ConnectCallback` throws an
`InvalidOperationException`, rather than losing the callback silently.

## Next steps

- [Configuration](03-Configuration.md): allow an internal host, change the blocked networks.
- [Handling rejections](04-HandlingRejections.md): log and count the rejected requests.
