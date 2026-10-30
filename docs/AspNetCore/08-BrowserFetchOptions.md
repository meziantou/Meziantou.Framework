---
title: Browser fetch options
description: Set the browser cache, credentials and request mode of every HttpClient request in a Blazor WebAssembly application.
---

In Blazor WebAssembly, `HttpClient` sends requests with the `fetch` API of the browser, and each request can carry
fetch options: whether the browser cache is used, whether cookies are sent, and the CORS mode.
`DefaultBrowserOptionsMessageHandler`, from `Meziantou.AspNetCore.Components.WebAssembly`, sets these options on every
request of a client.

```bash
dotnet add package Meziantou.AspNetCore.Components.WebAssembly
```

Add the handler to the client in `Program.cs`. `DefaultBrowserOptionsMessageHandler` is in the
[Meziantou.AspNetCore.Components.WebAssembly](cref:Meziantou.AspNetCore.Components.WebAssembly) namespace, and `BrowserRequestCache` in
`Microsoft.AspNetCore.Components.WebAssembly.Http`:

<!-- ref:../../samples/BlazorAppSample/ReadmeExamples/ComponentsReadmeExamples.cs;region=DefaultBrowserOptionsMessageHandler_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Add HttpClient with default browser options
builder.Services.AddHttpClient<MyApiClient>()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler(() => new DefaultBrowserOptionsMessageHandler()
    {
        DefaultBrowserRequestCache = BrowserRequestCache.NoCache
    });

await builder.Build().RunAsync();
```
<!-- endref -->

Every request of `MyApiClient` now bypasses the browser cache, so the application never shows a stale API response.

To create an `HttpClient` without `IHttpClientFactory`, pass the inner handler to the constructor:
`new DefaultBrowserOptionsMessageHandler(innerHandler)`.

## Options

| Property | Fetch option | Fetch default when not set |
| --- | --- | --- |
| `DefaultBrowserRequestCache` | `cache` | `default` |
| `DefaultBrowserRequestCredentials` | `credentials` | `same-origin` |
| `DefaultBrowserRequestMode` | `mode` | `cors` |

Only the properties you set are sent to the browser. A property left unset adds nothing to the request, so the
default of the fetch API applies. In the example above, only the `cache` option is set: cookies and the CORS mode are
not changed.

Set `DefaultBrowserRequestCredentials` to `BrowserRequestCredentials.Include` to send cookies with cross-origin
requests. It applies to every request of the client, to any origin: only use it on a client dedicated to a single
trusted API.

## Options of a request win

An option set on a request is never replaced by the handler:

<!-- ref:../../tests/Meziantou.AspNetCore.Components.WebAssembly.Tests/DefaultBrowserOptionsMessageHandlerTests.cs;region=DefaultBrowserOptionsMessageHandler_RequestOptionsWin;format=md-fenced-code-block;dedent=true -->
```csharp
// This request keeps force-cache, whatever the handler is configured with
request.SetBrowserRequestCache(BrowserRequestCache.ForceCache);
```
<!-- endref -->

Use the handler for the default of the client, and `SetBrowserRequestCache`, `SetBrowserRequestCredentials` or
`SetBrowserRequestMode` on the requests that need something else.

## Upgrading from a version before 2.1.0

Before version 2.1.0, the handler sent every option, using the default enum value for the ones you did not set:
`credentials: "omit"` and `mode: "same-origin"` were applied even when only the cache was configured, which broke
cookie authentication and cross-origin requests. If you relied on that behavior, set `DefaultBrowserRequestCredentials`
and `DefaultBrowserRequestMode` explicitly.
