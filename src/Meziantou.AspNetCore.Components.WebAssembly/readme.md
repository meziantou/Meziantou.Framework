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

## Additional Resources

- [Bypass browser cache using HttpClient in Blazor WebAssembly](https://www.meziantou.net/bypass-browser-cache-using-httpclient-in-blazor-webassembly.htm)
- [Fetch API Documentation](https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API)
