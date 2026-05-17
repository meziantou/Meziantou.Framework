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
