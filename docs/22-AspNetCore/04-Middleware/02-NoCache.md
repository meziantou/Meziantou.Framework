---
title: Default no-cache header
description: Make responses non-cacheable unless they set their own Cache-Control header, and place the middleware correctly.
---

`UseNoCache` adds `Cache-Control: no-cache,no-store,must-revalidate` to the responses that do not define
`Cache-Control`:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/AspNetCoreExamples.cs;region=NoCacheMiddleware_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNoCache();

app.MapGet("/", static () => "Hello World!");

app.Run();
```
<!-- endref -->

Without a `Cache-Control` header, browsers and proxies may decide on their own to cache a response (heuristic
caching), and a user can see an outdated page or API result. With this middleware, caching is something an endpoint
opts into.

## Behavior

- A response that sets its own `Cache-Control` is never modified. An empty value counts as not set.
- When the response has already started, the header is left alone rather than failing the request.
- `no-store` prevents any cache from keeping the response, including `UseResponseCaching`, `UseOutputCache` and a CDN.

## Placement

Register `UseNoCache` **after** the middleware that serve cacheable content, such as `UseStaticFiles`. The static file
middleware sets `ETag` and `Last-Modified` but no `Cache-Control`: when `UseNoCache` runs first, every static file
becomes non-cacheable.

To cache an endpoint, set the header in the endpoint, for instance with `context.Response.Headers.CacheControl =
"public, max-age=3600"`, or with the response caching or output caching features of ASP.NET Core that set it.
