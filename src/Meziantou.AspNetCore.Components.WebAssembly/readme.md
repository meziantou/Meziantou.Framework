# Meziantou.AspNetCore.Components.WebAssembly

A .NET library providing services and utilities for Blazor WebAssembly applications.

## Features

### DefaultBrowserOptionsMessageHandler

A message handler that allows you to set default browser options for all HTTP requests in your Blazor WebAssembly application. This is particularly useful for controlling browser cache behavior, credentials, and request modes.

#### Usage

Register the handler with `HttpClient` using dependency injection:

```csharp
using Meziantou.AspNetCore.Components.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

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

#### Behavior

Only the options you set on the handler are applied. An option you leave unset is **not** sent to the
browser, so the [Fetch defaults](https://fetch.spec.whatwg.org/#requestinit) keep applying
(`cache: "default"`, `credentials: "same-origin"`, `mode: "cors"`). In the example above, only the
`cache` option is set; credentials and mode are untouched.

Options already set on an individual request always win over the handler defaults:

```csharp
// This request keeps force-cache, whatever the handler is configured with
request.SetBrowserRequestCache(BrowserRequestCache.ForceCache);
```

> [!NOTE]
> Before version 2.1.0, unset properties were sent using their `default` enum value, which meant
> `credentials: "omit"` and `mode: "same-origin"` were applied even when only the cache was configured.
> That broke cookie-based authentication and cross-origin requests. If you relied on that behavior, set
> `DefaultBrowserRequestCredentials` and `DefaultBrowserRequestMode` explicitly.

## Additional Resources

- [Bypass browser cache using HttpClient in Blazor WebAssembly](https://www.meziantou.net/bypass-browser-cache-using-httpclient-in-blazor-webassembly.htm)
- [Fetch API Documentation](https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API)
