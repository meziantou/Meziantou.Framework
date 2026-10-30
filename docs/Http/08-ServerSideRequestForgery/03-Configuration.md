---
title: Configuration
description: Choose the allowed schemes, the blocked and allowed networks, how mixed DNS answers are handled and which address is used.
---

`ServerSideRequestForgeryOptions` holds the policy. Its collections start with safe defaults, and the example below
adds to them rather than replacing them:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryOptionsTests.cs;region=ServerSideRequestForgery_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
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
<!-- endref -->

| Option | Default | Meaning |
| --- | --- | --- |
| `SafeSchemes` | `https`, `wss` | The schemes a request may use, compared case-insensitively. |
| `UnsafeIpNetworks` | See below | The networks a connection must not reach. |
| `SafeIpNetworks` | Empty | The networks a connection may reach even when `UnsafeIpNetworks` contains them. |
| `DisallowMixedSafeAndUnsafeIpAddresses` | `true` | Reject a host name that resolves to both allowed and blocked addresses. |
| `ResolutionStrategy` | `PreferIpv4` | Which allowed address to connect to. |
| `Logger` | `NullLogger.Instance` | Where rejections are logged. See [Handling rejections](04-HandlingRejections.md). |

The options are read on every connection, after the handler is created.

## Allowed schemes

`SafeSchemes` contains `https` and `wss`. A request with another scheme, `http` included, is rejected. Call `Clear()`
first to define the list from scratch:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryOptionsTests.cs;region=ServerSideRequestForgery_ClearDefaults;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();
options.SafeSchemes.Clear();
options.SafeSchemes.Add("https");
```
<!-- endref -->

## Blocked networks

`UnsafeIpNetworks` starts with the ranges that are not public destinations:

| Networks | What they are |
| --- | --- |
| `0.0.0.0/8`, `::/96` | Unspecified addresses, and the deprecated IPv4-compatible IPv6 range |
| `127.0.0.0/8`, `::1/128` | Loopback |
| `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | Private IPv4 networks |
| `fc00::/7`, `fec0::/10` | Private IPv6 networks (unique local and site-local) |
| `169.254.0.0/16`, `fe80::/10` | Link-local, including the cloud metadata address `169.254.169.254` |
| `100.64.0.0/10` | Carrier-grade NAT |
| `224.0.0.0/4`, `ff00::/8` | Multicast |
| `192.0.0.0/24`, `192.88.99.0/24`, `198.18.0.0/15`, `240.0.0.0/4` | Reserved, deprecated, benchmarking and experimental ranges |
| `::ffff:0:0:0/96`, `64:ff9b::/96`, `64:ff9b:1::/48`, `2001::/32`, `2002::/16` | IPv6 ranges that embed an IPv4 address (IPv4-translated, NAT64, Teredo, 6to4) |

An IPv4 address written as an IPv6 address (`::ffff:127.0.0.1`) is checked as the IPv4 address it maps. An address that
is neither IPv4 nor IPv6 is always rejected.

Add the networks of your own infrastructure that are public but must not be reached, such as the public addresses of
your internal services.

## Allow an internal host

`SafeIpNetworks` takes precedence: an address it contains is allowed even when `UnsafeIpNetworks` contains it too. Use
it to reach one internal service without opening the range around it:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryClientHandlerTests.cs;region=ServerSideRequestForgery_AllowInternalHost;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();

// Allow one internal service, reached over plain HTTP. The rest of 127.0.0.0/8 stays blocked.
options.SafeSchemes.Add("http");
options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.1/32"));

var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);
using var httpClient = new HttpClient(handler, disposeHandler: true);

var text = await httpClient.GetStringAsync(new Uri($"http://127.0.0.1:{port}/"));
Assert.Equal("ok", text);
```
<!-- endref -->

Keep the entries narrow: a broad network in `SafeIpNetworks` silently re-opens every blocked address it covers.

## Host names with mixed addresses

A host name can resolve to several addresses, some allowed and some blocked. With
`DisallowMixedSafeAndUnsafeIpAddresses` set to `true` (the default), such a host is rejected: a DNS answer that mixes
a public and a private address can be an attempt to reach an internal host. Set it to `false` to drop the blocked addresses and connect to
one of the others.

A host name whose addresses are all blocked is always rejected.

## Choose the address to connect to

`ResolutionStrategy` picks the address among the allowed ones:

| Strategy | Address |
| --- | --- |
| `IpAddressResolutionStrategy.PreferIpv4` (default) | The first IPv4 address, or the first address when there is none |
| `IpAddressResolutionStrategy.Ipv4Only` | The first IPv4 address; a host with none is rejected |
| `IpAddressResolutionStrategy.Ipv6Only` | The first IPv6 address; a host with none is rejected |
| `IpAddressResolutionStrategy.Random` | A random address |
| `IpAddressResolutionStrategy.RoundRobin` | Each address in turn, one connection after the other |

When the connection to the chosen address fails, the strategy is asked again among the addresses that remain, so its
constraint holds: `Ipv4Only` never falls back to IPv6, and `PreferIpv4` tries IPv6 only once every IPv4 address failed.

To implement another rule, derive from `IpAddressResolutionStrategy` and override `ResolveAsync`. It receives the
allowed addresses and the options, and returns the address to connect to. Returning an address that is not in the list
rejects the request, and so does throwing a `ServerSideRequestForgeryException`.

## Name resolution

The host name of a request is resolved each time a connection is opened, and the results are not cached. A host name
that resolves to a public address when it is checked and to a private one when it is used (DNS rebinding) is therefore
validated against the address the connection actually uses. An IP address in the URL is used as is, without a DNS
query.

Every connection is validated, including the ones `SocketsHttpHandler` opens to follow a redirect.
