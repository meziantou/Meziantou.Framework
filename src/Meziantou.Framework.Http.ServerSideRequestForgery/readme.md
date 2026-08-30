# Meziantou.Framework.Http.ServerSideRequestForgery

SSRF protection for `SocketsHttpHandler` using scheme allow-listing and runtime IP validation.

## Usage

```csharp
using Meziantou.Framework.Http.ServerSideRequestForgery;

var options = new ServerSideRequestForgeryOptions
{
    ResolutionStrategy = IpAddressResolutionStrategy.PreferIpv4,
    DisallowMixedSafeAndUnsafeIpAddresses = true,
};

options.SafeSchemes.Add("https");
options.SafeSchemes.Add("wss");
options.UnsafeIpNetworks.Add(IPNetwork.Parse("203.0.113.0/24"));
options.SafeIpNetworks.Add(IPNetwork.Parse("198.51.100.10/32"));

var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);

using var httpClient = new HttpClient(handler, disposeHandler: true);
```

`ServerSideRequestForgeryClientHandler` configures the inner `SocketsHttpHandler` and rejects the requests that
would bypass it over HTTP/3. `SocketsHttpHandler.ConfigureSsrf(options)` applies the connection validation on its
own, but leaves the HTTP/3 gap below open, so prefer the handler.

## Behavior

- Validates request scheme against `SafeSchemes`.
- Resolves DNS on every connection attempt to avoid TOCTOU vulnerabilities.
- Validates each resolved address against `UnsafeIpNetworks` and `SafeIpNetworks`.
- Optionally rejects mixed safe/unsafe DNS responses.
- Uses `IpAddressResolutionStrategy` to select the final address (`Ipv4Only`, `Ipv6Only`, `PreferIpv4`, `Random`, `RoundRobin`).
- Rejects connections that target an HTTP proxy.
- Rejects requests that would be sent over HTTP/3, whose QUIC connection cannot be validated.

## Proxies

Validation happens when the connection is opened, which for a proxied request is the connection to the
*proxy*, not to the target. The proxy then reaches the real target itself, over a tunnel this library
cannot inspect, so a proxied request cannot be validated at all.

Rather than appear to protect such a request, the handler rejects it with a
`ServerSideRequestForgeryException`. Note that `SocketsHttpHandler.UseProxy` defaults to `true` and
`HttpClient.DefaultProxy` reads `HTTP_PROXY`, `HTTPS_PROXY` and `ALL_PROXY`, so a proxy can be in effect
without the application configuring one. Send requests that need SSRF protection through a handler with
`UseProxy = false`:

```csharp
var handler = new SocketsHttpHandler { UseProxy = false };
handler.ConfigureSsrf(options);
```

Requests the proxy is configured to bypass are connected to directly and are validated normally.

## HTTP/3

Validation runs from `SocketsHttpHandler.ConnectCallback`, which the runtime uses only for TCP connections. An
HTTP/3 connection is established over QUIC by `ConnectHelper.ConnectQuicAsync`, which resolves the endpoint itself
and never calls the callback, so **an HTTP/3 request is not validated at all**. Setting a `ConnectCallback` does not
disable HTTP/3: `HttpConnectionPool` clears it for plaintext HTTP and for every proxy kind, but not for a direct
HTTPS connection, and HTTP/3 is enabled by default on Windows, Linux and macOS.

Two requests reach QUIC:

- one that asks for it — `Version` 3.0 with a policy other than `RequestVersionOrLower`;
- one that merely allows an upgrade — `VersionPolicy = RequestVersionOrHigher` over TLS. The server then only has
  to answer with an `Alt-Svc: h3="..."` header, which may name **any** host and port, and the next request to that
  authority goes to it over QUIC. The first, validated connection buys nothing.

`ServerSideRequestForgeryClientHandler` rejects both with a `ServerSideRequestForgeryException`, the same way a
proxied request is rejected, rather than appear to protect a request it cannot see. A request is accepted when its
`Version` is below 3.0 *and* its `VersionPolicy` is not `RequestVersionOrHigher`; HTTP/2 is still negotiated over
TLS under those settings.

The check is on the version alone and not on the scheme, because `SocketsHttpHandler` follows redirects below this
handler: a plaintext request that redirects to HTTPS would otherwise slip through.

If you call `ConfigureSsrf` directly instead of using the handler, HTTP/3 must be ruled out some other way — the
`DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP3SUPPORT=0` environment variable, the matching `AppContext` switch,
or `HttpClient.DefaultRequestVersion` and `HttpClient.DefaultVersionPolicy` on every client.
