---
title: Getting started
description: Install the package you need, add its handler to an HttpClient, or register it with IHttpClientFactory.
---

## Install a package

Each feature is its own package. Add the ones you need to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.Http.Caching
dotnet add package Meziantou.Framework.Http.Hsts
dotnet add package Meziantou.Framework.Http.ServerSideRequestForgery
```

The [introduction](01-Introduction.md#packages) lists every package. The types are in these namespaces:

| Package | Namespace |
| --- | --- |
| `Meziantou.Framework.Http`, `.Hsts`, `.Htpasswd` | [Meziantou.Framework.Http](cref:Meziantou.Framework.Http) |
| `Meziantou.Framework.Http.Caching` | [Meziantou.Framework.Http.Caching](cref:Meziantou.Framework.Http.Caching) |
| `Meziantou.Framework.Http.Caching.InMemory`, `.Sqlite`, `.Redis` | [Meziantou.Framework.Http.Caching.InMemory](cref:Meziantou.Framework.Http.Caching.InMemory), `.Sqlite`, `.Redis` |
| `Meziantou.Framework.Http.Recording` | [Meziantou.Framework.Http.Recording](cref:Meziantou.Framework.Http.Recording) |
| `Meziantou.Framework.Http.ServerSideRequestForgery` | [Meziantou.Framework.Http.ServerSideRequestForgery](cref:Meziantou.Framework.Http.ServerSideRequestForgery) |

The examples of this documentation omit the `using` directives.

## Add a handler to an HttpClient

The caching, HSTS, SSRF and recording features are `DelegatingHandler`s. Create the handler with the handler that sends
the requests, usually a `SocketsHttpHandler`, and give it to the `HttpClient`:

<!-- ref:../../samples/Meziantou.Framework.Http.HstsSample/ReadmeExamples.cs;region=HstsClientHandler_UpgradePreloadedHost;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

// Automatically upgrade to HTTPS as github.com is in the HSTS preload list
using var response = await client.GetAsync("http://github.com");
```
<!-- endref -->

Every request sent by this client goes through `HstsClientHandler` before it reaches the network. With
`disposeHandler: true`, disposing the client disposes the handlers.

A handler is a link of a chain: its inner handler can itself be a `DelegatingHandler`. Each page says what a handler
needs from the handlers below it; for instance, the SSRF protection needs a `SocketsHttpHandler`, and the HSTS handler
follows the redirects itself.

## Register a handler with IHttpClientFactory

With `IHttpClientFactory` (the `Microsoft.Extensions.Http` package), register the handler in the container and add it
to a named or typed client with `AddHttpMessageHandler`. The factory creates the primary handler and sets the
`InnerHandler`, so use a constructor that takes no inner handler:

<!-- ref:../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsClientHandlerTests.cs;region=HstsClientHandler_DependencyInjection;format=md-fenced-code-block;dedent=true -->
```csharp
services.AddTransient(_ => new HstsClientHandler(policies));
services.AddHttpClient("api")
        .AddHttpMessageHandler<HstsClientHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler());
```
<!-- endref -->

`AddHttpMessageHandler` fails when the handler already has an inner handler.

## Next steps

- [Caching](04-Caching/01-Overview.md): cache the responses of an `HttpClient`.
- [HSTS](05-Hsts/01-Overview.md): send the requests to HSTS hosts over HTTPS.
- [Server-side request forgery](08-ServerSideRequestForgery/01-Overview.md): fetch untrusted URLs safely.
- [Recording](07-Recording/01-Overview.md): record and replay HTTP traffic in tests.
- [Link headers](03-LinkHeaders.md) and [Htpasswd files](06-Htpasswd.md): parse HTTP formats.
