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

var handler = new SocketsHttpHandler();
handler.ConfigureSsrf(options);

using var httpClient = new HttpClient(handler, disposeHandler: true);
```

## Behavior

- Validates request scheme against `SafeSchemes`.
- Resolves DNS on every connection attempt to avoid TOCTOU vulnerabilities.
- Validates each resolved address against `UnsafeIpNetworks` and `SafeIpNetworks`.
- Optionally rejects mixed safe/unsafe DNS responses.
- Uses `IpAddressResolutionStrategy` to select the final address (`Ipv4Only`, `Ipv6Only`, `PreferIpv4`, `Random`, `RoundRobin`).
- Rejects connections that target an HTTP proxy.

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
